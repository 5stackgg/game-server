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
    Description = "Enforces 5Stack bans, mutes and gags on community servers"
)]
public class PlayerManagementPlugin : BasePlugin
{
    private const string Runtime = "swiftlys2";

    // Doubles as the heartbeat the panel reads to show the plugin as active,
    // which is why an empty server still syncs.
    private const int SyncSeconds = 30;

    private ILogger<PlayerManagementPlugin> _logger = null!;
    private IConfiguration _configuration = null!;
    private ServiceProvider? _serviceProvider;

    private readonly SanctionsClient _client = new();
    private readonly SanctionBook _book = new();

    // What this plugin last applied per player, so it only ever lifts a mute it
    // set itself and never one an admin plugin on the same server set.
    private readonly Dictionary<ulong, SanctionState> _applied = new();
    private readonly HashSet<ulong> _kicked = new();

    private CancellationTokenSource? _secondTimer;
    private EventDelegates.OnClientPutInServer? _putInServerHandler;
    private EventDelegates.OnClientSteamAuthorize? _authorizeHandler;
    private EventDelegates.OnClientDisconnected? _disconnectHandler;
    private Guid _chatHookId;

    private int _secondsSinceSync = SyncSeconds;
    private bool _syncing;
    private bool _syncRequested;
    private DateTimeOffset? _lastSyncAt;
    private string? _lastError;

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
                "player management is not configured; bans, mutes and gags are not enforced until API_DOMAIN, SERVER_ID and SERVER_API_PASSWORD are set"
            );
        }

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

            _applied.Remove(SteamIdOf(player));
            _kicked.Remove(SteamIdOf(player));
        };
        Core.Event.OnClientDisconnected += _disconnectHandler;

        _chatHookId = Core.Command.HookClientChat((playerId, text, teamonly) => OnChat(playerId));

        _secondTimer = Core.Scheduler.RepeatBySeconds(1, OnSecond);
    }

    public override void Unload()
    {
        _secondTimer?.Cancel();
        _secondTimer = null;

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

        RequestSync();

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

        context.Reply(
            PlayerManagementReport.Status(
                ModuleVersion,
                Runtime,
                Settings(),
                _lastSyncAt,
                _lastError,
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

    private void OnJoined(int playerId)
    {
        IPlayer? player = Core.PlayerManager.GetPlayer(playerId);

        if (player == null || !IsHuman(player))
        {
            return;
        }

        // The cached answer covers a panel that is unreachable right now; the
        // sync that follows replaces it with the current one.
        Enforce();
        RequestSync();
    }

    private void OnSecond()
    {
        if (++_secondsSinceSync >= SyncSeconds)
        {
            RequestSync();
        }

        // Every second rather than every sync, so a timed mute lifts when it
        // runs out instead of up to a sync later.
        Enforce();
    }

    private void RequestSync()
    {
        if (_syncing)
        {
            // The sync in flight predates whatever asked for this one.
            _syncRequested = true;
            return;
        }

        PlayerManagementSettings settings = Settings();

        _secondsSinceSync = 0;

        if (!settings.IsConnected())
        {
            return;
        }

        List<string> steamIds = Humans()
            .Select(player => SteamIdOf(player).ToString())
            .Distinct()
            .ToList();

        PlayerSanctionsRequest body = new()
        {
            steam_ids = steamIds,
            plugin_version = ModuleVersion,
            plugin_runtime = Runtime,
        };

        _syncing = true;
        _syncRequested = false;

        _ = Task.Run(async () =>
        {
            SanctionSync result = await _client.Sync(settings, body);

            Core.Scheduler.NextTick(() => Synced(steamIds, result));
        });
    }

    private void Synced(List<string> steamIds, SanctionSync result)
    {
        _syncing = false;

        if (result.Sanctions == null)
        {
            if (result.Error != _lastError)
            {
                _logger.LogWarning("unable to sync sanctions: {error}", result.Error);
            }

            _lastError = result.Error;
        }
        else
        {
            if (_lastError != null)
            {
                _logger.LogInformation("sanction sync recovered");
            }

            _lastError = null;
            _lastSyncAt = DateTimeOffset.UtcNow;
            _book.Record(steamIds, result.Sanctions);

            Enforce();
        }

        if (_syncRequested)
        {
            RequestSync();
        }
    }

    private void Enforce()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (IPlayer player in Humans())
        {
            ulong steamId = SteamIdOf(player);
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

            // Re-asserted rather than set once: the engine resets voice flags
            // across a reconnect and a map change.
            if (state.IsMuted && player.VoiceFlags != VoiceFlagValue.Muted)
            {
                player.VoiceFlags = VoiceFlagValue.Muted;
            }
            else if (changes.HasFlag(eSanctionChange.Unmuted))
            {
                player.VoiceFlags = VoiceFlagValue.Normal;
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
