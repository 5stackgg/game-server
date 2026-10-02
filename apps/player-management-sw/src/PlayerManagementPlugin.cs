using System.Reflection;
using FiveStack.Entities.PlayerManagement;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.ProtobufDefinitions;
using SwiftlyS2.Shared.Translation;

namespace PlayerManagement;

// Community servers only: a matchmaking server gets its sanctions on the match
// payload from the match plugin, and never loads this one.
[PluginMetadata(
    Id = "PlayerManagement",
    Version = "__RELEASE_VERSION__",
    Name = "5stack-player-management",
    Author = "5Stack.gg",
    Description = "Enforces 5Stack bans, mutes, gags and access lists on community servers"
)]
public partial class PlayerManagementPlugin : BasePlugin
{
    private const string Runtime = "swiftlys2";

    private const long EnforceEveryMs = 1000;

    private ILogger<PlayerManagementPlugin> _logger = null!;
    private IConfiguration _configuration = null!;
    private ServiceProvider? _serviceProvider;
    private SanctionSyncLoop? _loop;

    private readonly SanctionBook _book = new();
    private readonly ServerAccessBook _access = new();
    private readonly PlayerRoster _roster = new();

    // What was last applied to each player present, so changes are announced
    // once, and whose mute bit this plugin set, so it only ever lifts its own
    // and never one an admin plugin on the same server set.
    private readonly Dictionary<ulong, SanctionState> _applied = new();
    private readonly HashSet<ulong> _mutedByUs = new();
    private readonly HashSet<ulong> _kicked = new();

    private EventDelegates.OnTick? _tickHandler;
    private EventDelegates.OnClientPutInServer? _putInServerHandler;
    private EventDelegates.OnClientSteamAuthorize? _authorizeHandler;
    private EventDelegates.OnClientDisconnected? _disconnectHandler;
    private EventDelegates.OnMapUnload? _mapUnloadHandler;
    private EventDelegates.OnMapLoad? _mapLoadHandler;
    private EventDelegates.OnWorldUpdate? _worldUpdateHandler;
    private Guid _chatHookId;
    private long _lastEnforceMs;
    private long _lastWorldUpdateMs;

    public PlayerManagementPlugin(ISwiftlyCore core)
        : base(core) { }

    public string ModuleVersion =>
        typeof(PlayerManagementPlugin).GetCustomAttribute<PluginMetadata>()?.Version ?? "unknown";

    public override void Load(bool hotReload)
    {
        Core.Configuration.InitializeJsonWithModel<PlayerManagementSettings>(
                "config.jsonc",
                "PlayerManagement"
            )
            .Configure(builder =>
            {
                builder.AddJsonFile("config.jsonc", optional: true, reloadOnChange: true);
            });

        _configuration = Core.Configuration.Manager;

        ServiceCollection services = new();
        services.AddSwiftly(Core);
        _serviceProvider = services.BuildServiceProvider();
        _logger = _serviceProvider.GetRequiredService<ILogger<PlayerManagementPlugin>>();

        PlayerManagementSettings settings = Settings();

        _logger.LogInformation(
            "player management {version} loaded; panel {api}, configured: {configured}",
            ModuleVersion,
            settings.API_DOMAIN,
            settings.IsConnected()
        );

        if (!settings.IsConnected())
        {
            _logger.LogWarning(
                "player management is not configured; bans, mutes, gags and access lists are not enforced until API_DOMAIN, SERVER_ID and SERVER_API_PASSWORD are set"
            );
        }

        _tickHandler = OnTick;
        Core.Event.OnTick += _tickHandler;

        _putInServerHandler = @event => OnJoined(@event.PlayerId);
        Core.Event.OnClientPutInServer += _putInServerHandler;

        // Joining and Steam authorizing race each other, and the id a player is
        // enforced by can change from the claimed one to the verified one.
        _authorizeHandler = @event => OnJoined(@event.PlayerId);
        Core.Event.OnClientSteamAuthorize += _authorizeHandler;

        _disconnectHandler = @event =>
        {
            IPlayer? player = Core.PlayerManager.GetPlayer(@event.PlayerId);

            if (player == null)
            {
                return;
            }

            ulong steamId = SteamIdOf(player);

            _book.Left(steamId.ToString());
            _access.Left(steamId.ToString());
            _applied.Remove(steamId);
            _mutedByUs.Remove(steamId);
            _kicked.Remove(steamId);
        };
        Core.Event.OnClientDisconnected += _disconnectHandler;

        _mapUnloadHandler = _ => _roster.MapEnded(DateTimeOffset.UtcNow);
        Core.Event.OnMapUnload += _mapUnloadHandler;

        _mapLoadHandler = _ => _roster.MapStarted(DateTimeOffset.UtcNow);
        Core.Event.OnMapLoad += _mapLoadHandler;

        _worldUpdateHandler = OnWorldUpdate;
        Core.Event.OnWorldUpdate += _worldUpdateHandler;

        _chatHookId = Core.Command.HookClientChat((playerId, text, teamonly) => OnChat(playerId));

        InstallConnectGate();

        _loop = new SanctionSyncLoop(
            _book,
            _access,
            _roster,
            new SanctionsClient(),
            Settings,
            ModuleVersion,
            Runtime,
            message => _logger.LogWarning("{message}", message),
            message => _logger.LogInformation("{message}", message)
        );

        // So a hot reload reports the players already here, not an unknown
        // roster. Before a map has loaded there may be no list to read, and the
        // roster is then left unknown until the first tick.
        try
        {
            _loop.Observe(Observed(Humans()), DateTimeOffset.UtcNow);
        }
        catch (Exception error)
        {
            _logger.LogDebug(error, "unable to read the players present on load");
        }

        _loop.Start();
    }

    public override void Unload()
    {
        _loop?.Dispose();
        _loop = null;

        UninstallConnectGate();

        if (_tickHandler != null)
        {
            Core.Event.OnTick -= _tickHandler;
        }

        if (_putInServerHandler != null)
        {
            Core.Event.OnClientPutInServer -= _putInServerHandler;
        }

        if (_authorizeHandler != null)
        {
            Core.Event.OnClientSteamAuthorize -= _authorizeHandler;
        }

        if (_disconnectHandler != null)
        {
            Core.Event.OnClientDisconnected -= _disconnectHandler;
        }

        if (_mapUnloadHandler != null)
        {
            Core.Event.OnMapUnload -= _mapUnloadHandler;
        }

        if (_mapLoadHandler != null)
        {
            Core.Event.OnMapLoad -= _mapLoadHandler;
        }

        if (_worldUpdateHandler != null)
        {
            Core.Event.OnWorldUpdate -= _worldUpdateHandler;
        }

        if (_chatHookId != Guid.Empty)
        {
            Core.Command.UnhookClientChat(_chatHookId);
        }

        _serviceProvider?.Dispose();
    }

    [Command("player_management_refresh", registerRaw: true, permission: "")]
    public void OnRefresh(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            return;
        }

        if (!Settings().IsConnected())
        {
            context.Reply(PlayerManagementReport.NotConfigured());
            return;
        }

        _loop?.Request();

        context.Reply(PlayerManagementReport.Syncing(Humans().Count));
    }

    [Command("player_management_status", registerRaw: true, permission: "")]
    public void OnStatus(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        (DateTimeOffset? lastSyncAt, string? lastError) = _loop?.Status() ?? (null, null);

        context.Reply(
            PlayerManagementReport.Status(
                ModuleVersion,
                Runtime,
                Settings(),
                lastSyncAt,
                lastError,
                _access.Snapshot(),
                Humans()
                    .Select(player => new PlayerManagementPlayer(
                        player.Name,
                        SteamIdOf(player).ToString(),
                        _book.StateFor(SteamIdOf(player).ToString(), now)
                    ))
                    .ToList(),
                now
            )
        );
    }

    // A player may not be valid yet at either join event, so only the id is
    // required here; the sync asks about them either way.
    private void OnJoined(int playerId)
    {
        IPlayer? player = Core.PlayerManager.GetPlayer(playerId);

        if (player == null || player.IsFakeClient || SteamIdOf(player) == 0)
        {
            return;
        }

        _book.Joined(SteamIdOf(player).ToString());
        _loop?.Request();
    }

    // Throttled off the tick rather than a repeating timer: SwiftlyS2 replays
    // every missed run of a timer after hibernation, and a timer whose
    // callback throws is never scheduled again.
    private void OnTick()
    {
        long nowMs = Environment.TickCount64;

        if (nowMs - _lastEnforceMs < EnforceEveryMs)
        {
            return;
        }

        _lastEnforceMs = nowMs;

        try
        {
            List<IPlayer> humans = Humans();

            ObserveRoster(humans);
            Enforce(humans);
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to enforce sanctions");
        }
    }

    private void Enforce(List<IPlayer> humans)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        HashSet<ulong> present = humans.Select(SteamIdOf).ToHashSet();

        foreach (ulong gone in _applied.Keys.Where(steamId => !present.Contains(steamId)).ToList())
        {
            _applied.Remove(gone);
        }

        _mutedByUs.RemoveWhere(steamId => !present.Contains(steamId));
        _kicked.RemoveWhere(steamId => !present.Contains(steamId));

        foreach (IPlayer player in humans)
        {
            ulong steamId = SteamIdOf(player);

            if (_book.IsAwaiting(steamId.ToString()))
            {
                continue;
            }

            SanctionState state = _book.StateFor(steamId.ToString(), now);
            SanctionState previous = _applied.GetValueOrDefault(steamId, SanctionState.None);
            eSanctionChange changes = SanctionState.Changes(previous, state);

            _applied[steamId] = state;

            if (changes.HasFlag(eSanctionChange.Banned))
            {
                if (_kicked.Add(steamId))
                {
                    _logger.LogInformation(
                        "kicking banned player {name} ({steamId})",
                        player.Name,
                        steamId
                    );

                    player.Kick(
                        SanctionBook.KickReason(state.Ban!),
                        ENetworkDisconnectionReason.NETWORK_DISCONNECT_BANADDED
                    );
                }

                continue;
            }

            if (_access.IsDenied(steamId.ToString()))
            {
                if (_kicked.Add(steamId))
                {
                    _logger.LogInformation(
                        "kicking {name} ({steamId}): not on the server's access list",
                        player.Name,
                        steamId
                    );

                    player.Kick(
                        _access.KickReason(
                            Core.Translation.GetPlayerLocalizer(player)["access.denied"]
                        ),
                        ENetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED
                    );
                }

                continue;
            }

            // Re-asserted rather than set once: the engine resets voice flags
            // across a reconnect and a map change.
            if (state.IsMuted && !player.VoiceFlags.HasFlag(VoiceFlagValue.Muted))
            {
                player.VoiceFlags |= VoiceFlagValue.Muted;
                _mutedByUs.Add(steamId);
            }
            else if (!state.IsMuted && _mutedByUs.Remove(steamId))
            {
                player.VoiceFlags &= ~VoiceFlagValue.Muted;
            }

            if (changes == eSanctionChange.None)
            {
                continue;
            }

            ILocalizer localizer = Core.Translation.GetPlayerLocalizer(player);

            if (changes.HasFlag(eSanctionChange.Muted))
            {
                Tell(player, localizer, "sanction.muted", state.Mute!);
            }
            else if (changes.HasFlag(eSanctionChange.Unmuted))
            {
                player.SendChat(localizer["sanction.unmuted"]);
            }

            if (changes.HasFlag(eSanctionChange.Gagged))
            {
                Tell(player, localizer, "sanction.gagged", state.Gag!);
            }
            else if (changes.HasFlag(eSanctionChange.Ungagged))
            {
                player.SendChat(localizer["sanction.ungagged"]);
            }
        }
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnPlayerDeath(EventPlayerDeath @event)
    {
        try
        {
            IPlayer? victim = @event.UserIdPlayer;

            if (victim == null || !IsHuman(victim))
            {
                return HookResult.Continue;
            }

            IPlayer? attacker = @event.AttackerPlayer;

            bool kill =
                attacker != null
                && IsHuman(attacker)
                && PlayerRoster.CountsAsKill(
                    attacker.PlayerID,
                    victim.PlayerID,
                    attacker.Controller.TeamNum,
                    victim.Controller.TeamNum,
                    TeammatesAreEnemies()
                );

            _roster.Died(VerifiedSteamIdOf(victim), kill ? VerifiedSteamIdOf(attacker!) : null);
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to count a death");
        }

        return HookResult.Continue;
    }

    // Kept apart from the enforcement that follows it on the tick, which must
    // run whatever happens to the roster.
    private void ObserveRoster(List<IPlayer> humans)
    {
        try
        {
            _loop?.Observe(Observed(humans), DateTimeOffset.UtcNow);
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to observe the players present");
        }
    }

    // Unreadable is taken as teams as usual, where a team kill earns nothing.
    private bool TeammatesAreEnemies()
    {
        try
        {
            return Core.ConVar.Find<bool>("mp_teammates_are_enemies")?.Value ?? false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Still called while the server hibernates, when OnTick is not, which is
    // how the roster tells an empty server from one loading a map.
    private void OnWorldUpdate()
    {
        long nowMs = Environment.TickCount64;

        if (nowMs - _lastWorldUpdateMs < EnforceEveryMs)
        {
            return;
        }

        _lastWorldUpdateMs = nowMs;
        _roster.WorldUpdated(DateTimeOffset.UtcNow);
    }

    private HookResult OnChat(int playerId)
    {
        IPlayer? player = Core.PlayerManager.GetPlayer(playerId);

        if (player == null || !IsHuman(player))
        {
            return HookResult.Continue;
        }

        SanctionState state = _book.StateFor(SteamIdOf(player).ToString(), DateTimeOffset.UtcNow);

        if (!state.IsGagged)
        {
            return HookResult.Continue;
        }

        Tell(player, Core.Translation.GetPlayerLocalizer(player), "sanction.gagged", state.Gag!);

        return HookResult.Stop;
    }

    private static void Tell(
        IPlayer player,
        ILocalizer localizer,
        string key,
        PlayerSanction sanction
    )
    {
        player.SendChat(localizer[key]);

        string reason = SanctionBook.Reason(sanction);
        if (reason.Length > 0)
        {
            player.SendChat(localizer["sanction.reason", reason]);
        }

        string until = SanctionBook.Until(sanction);
        player.SendChat(
            until.Length == 0 ? localizer["sanction.permanent"] : localizer["sanction.until", until]
        );
    }

    private List<IPlayer> Humans()
    {
        return Core.PlayerManager.GetAllPlayers().Where(IsHuman).ToList();
    }

    private static List<ObservedPlayer> Observed(List<IPlayer> humans)
    {
        return humans
            .Select(player => new ObservedPlayer(
                SteamIdOf(player).ToString(),
                player.Name,
                player.IsAuthorized ? player.IPAddress : null,
                player.IsAuthorized
            ))
            .ToList();
    }

    // Not IPlayer.IsValid: that also wants a pawn, and a player without one
    // is still on the server, so the roster ended their session and a gag
    // stopped applying until they had one again.
    private static bool IsHuman(IPlayer player)
    {
        CCSPlayerController controller = player.Controller;

        if (!controller.IsValid)
        {
            return false;
        }

        return PlayerRoster.IsConnectedHuman(
            controller.IsHLTV,
            controller.Connected == PlayerConnectedState.Connected,
            player.IsFakeClient,
            SteamIdOf(player)
        );
    }

    // Before Steam verifies a player only the id they claim is known. Enforcing
    // on it is safe because a sanction only ever takes something away: a player
    // who claims somebody else's id gains nothing, and can only inherit a ban.
    private static ulong SteamIdOf(IPlayer player)
    {
        return player.IsAuthorized ? player.SteamID : player.UnauthorizedSteamID;
    }

    // Unlike a sanction, a kill credited to a claimed id is something gained.
    private static string? VerifiedSteamIdOf(IPlayer player)
    {
        return player.IsAuthorized ? player.SteamID.ToString() : null;
    }

    private PlayerManagementSettings Settings()
    {
        PlayerManagementSettings file =
            _configuration.GetSection("PlayerManagement").Get<PlayerManagementSettings>()
            ?? new PlayerManagementSettings();

        return file.Resolve(Environment.GetEnvironmentVariable);
    }
}
