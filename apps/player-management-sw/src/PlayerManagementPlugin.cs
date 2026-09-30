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
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
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
    private Guid _chatHookId;
    private long _lastEnforceMs;

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

        _chatHookId = Core.Command.HookClientChat((playerId, text, teamonly) => OnChat(playerId));

        InstallConnectGate();

        _loop = new SanctionSyncLoop(
            _book,
            _access,
            new SanctionsClient(),
            Settings,
            ModuleVersion,
            Runtime,
            message => _logger.LogWarning("{message}", message),
            message => _logger.LogInformation("{message}", message)
        );
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

            _loop?.Observe(humans.Select(player => SteamIdOf(player).ToString()));

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

    private static bool IsHuman(IPlayer player)
    {
        return player.IsValid && !player.IsFakeClient && SteamIdOf(player) != 0;
    }

    // Before Steam verifies a player only the id they claim is known. Enforcing
    // on it is safe because a sanction only ever takes something away: a player
    // who claims somebody else's id gains nothing, and can only inherit a ban.
    private static ulong SteamIdOf(IPlayer player)
    {
        return player.IsAuthorized ? player.SteamID : player.UnauthorizedSteamID;
    }

    private PlayerManagementSettings Settings()
    {
        PlayerManagementSettings file =
            _configuration.GetSection("PlayerManagement").Get<PlayerManagementSettings>()
            ?? new PlayerManagementSettings();

        return file.Resolve(Environment.GetEnvironmentVariable);
    }
}
