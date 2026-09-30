using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.ValveConstants.Protobuf;
using FiveStack.Entities.PlayerManagement;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace PlayerManagement;

// Community servers only: a matchmaking server gets its sanctions on the match
// payload from the match plugin, and never loads this one.
[MinimumApiVersion(80)]
public class PlayerManagementPlugin : BasePlugin, IPluginConfig<PlayerManagementConfig>
{
    private const string Runtime = "counterstrikesharp";

    // Doubles as the heartbeat the panel reads to show the plugin as active,
    // which is why an empty server still syncs.
    private const int SyncSeconds = 30;

    public override string ModuleName => "PlayerManagement";
    public override string ModuleVersion => "__RELEASE_VERSION__";
    public override string ModuleAuthor => "5Stack.gg";
    public override string ModuleDescription =>
        "Enforces 5Stack bans, mutes and gags on community servers";

    public PlayerManagementConfig Config { get; set; } = new();

    private readonly SanctionsClient _client = new();
    private readonly SanctionBook _book = new();

    // What this plugin last applied per player, so it only ever lifts a mute it
    // set itself and never one an admin plugin on the same server set.
    private readonly Dictionary<ulong, SanctionState> _applied = new();
    private readonly HashSet<ulong> _kicked = new();

    private Timer? _secondTimer;

    private int _secondsSinceSync = SyncSeconds;
    private bool _syncing;
    private bool _syncRequested;
    private DateTimeOffset? _lastSyncAt;
    private string? _lastError;

    public void OnConfigParsed(PlayerManagementConfig config)
    {
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        PlayerManagementSettings settings = Config.Settings();

        Logger.LogInformation(
            "player management {version} loaded; panel {api}, configured: {configured}",
            ModuleVersion,
            settings.API_DOMAIN,
            settings.IsConnected()
        );

        if (!settings.IsConnected())
        {
            Logger.LogWarning(
                "player management is not configured; bans, mutes and gags are not enforced until API_DOMAIN, SERVER_ID and SERVER_API_PASSWORD are set"
            );
        }

        RegisterListener<Listeners.OnClientPutInServer>(OnJoined);

        // Joining and Steam authorizing race each other, and the id a player is
        // enforced by can change from the claimed one to the verified one.
        RegisterListener<Listeners.OnClientAuthorized>((slot, _) => OnJoined(slot));

        RegisterListener<Listeners.OnClientDisconnect>(slot =>
        {
            CCSPlayerController? player = Utilities.GetPlayerFromSlot(slot);

            if (player == null)
            {
                return;
            }

            _applied.Remove(SteamIdOf(player));
            _kicked.Remove(SteamIdOf(player));
        });

        AddCommandListener("say", OnChat, HookMode.Pre);
        AddCommandListener("say_team", OnChat, HookMode.Pre);

        _secondTimer = AddTimer(1f, OnSecond, TimerFlags.REPEAT);
    }

    public override void Unload(bool hotReload)
    {
        _secondTimer?.Kill();
        _secondTimer = null;
    }

    [ConsoleCommand(
        "player_management_refresh",
        "Syncs 5Stack sanctions for everyone on the server"
    )]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnRefresh(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Config.Settings().IsConnected())
        {
            command.ReplyToCommand(PlayerManagementReport.NotConfigured());
            return;
        }

        RequestSync();

        command.ReplyToCommand(PlayerManagementReport.Syncing(Humans().Count));
    }

    [ConsoleCommand("player_management_status", "Reports 5Stack player management state")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnStatus(CCSPlayerController? caller, CommandInfo command)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        command.ReplyToCommand(
            PlayerManagementReport.Status(
                ModuleVersion,
                Runtime,
                Config.Settings(),
                _lastSyncAt,
                _lastError,
                Humans()
                    .Select(player => new PlayerManagementPlayer(
                        player.PlayerName,
                        SteamIdOf(player).ToString(),
                        _book.StateFor(SteamIdOf(player).ToString(), now)
                    ))
                    .ToList(),
                now
            )
        );
    }

    private void OnJoined(int slot)
    {
        CCSPlayerController? player = Utilities.GetPlayerFromSlot(slot);

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

        PlayerManagementSettings settings = Config.Settings();

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

            Server.NextFrame(() => Synced(steamIds, result));
        });
    }

    private void Synced(List<string> steamIds, SanctionSync result)
    {
        _syncing = false;

        if (result.Sanctions == null)
        {
            if (result.Error != _lastError)
            {
                Logger.LogWarning("unable to sync sanctions: {error}", result.Error);
            }

            _lastError = result.Error;
        }
        else
        {
            if (_lastError != null)
            {
                Logger.LogInformation("sanction sync recovered");
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

        foreach (CCSPlayerController player in Humans())
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
                    Logger.LogInformation(
                        "kicking banned player {name} ({steamId})",
                        player.PlayerName,
                        steamId
                    );

                    player.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_BANADDED);
                }

                continue;
            }

            // Re-asserted rather than set once: the engine resets voice flags
            // across a reconnect and a map change.
            if (state.IsMuted && player.VoiceFlags != VoiceFlags.Muted)
            {
                player.VoiceFlags = VoiceFlags.Muted;
            }
            else if (changes.HasFlag(eSanctionChange.Unmuted))
            {
                player.VoiceFlags = VoiceFlags.Normal;
            }

            if (changes.HasFlag(eSanctionChange.Muted))
            {
                Tell(player, "sanction.muted", state.Mute!);
            }
            else if (changes.HasFlag(eSanctionChange.Unmuted))
            {
                player.PrintToChat(Localizer.ForPlayer(player, "sanction.unmuted"));
            }

            if (changes.HasFlag(eSanctionChange.Gagged))
            {
                Tell(player, "sanction.gagged", state.Gag!);
            }
            else if (changes.HasFlag(eSanctionChange.Ungagged))
            {
                player.PrintToChat(Localizer.ForPlayer(player, "sanction.ungagged"));
            }
        }
    }

    private HookResult OnChat(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !IsHuman(player))
        {
            return HookResult.Continue;
        }

        SanctionState state = _book.StateFor(SteamIdOf(player).ToString(), DateTimeOffset.UtcNow);

        if (!state.IsGagged)
        {
            return HookResult.Continue;
        }

        Tell(player, "sanction.gagged", state.Gag!);

        return HookResult.Stop;
    }

    private void Tell(CCSPlayerController player, string key, PlayerSanction sanction)
    {
        player.PrintToChat(Localizer.ForPlayer(player, key));

        string reason = SanctionBook.Reason(sanction);
        if (reason.Length > 0)
        {
            player.PrintToChat(Localizer.ForPlayer(player, "sanction.reason", reason));
        }

        string until = SanctionBook.Until(sanction);
        player.PrintToChat(
            until.Length == 0
                ? Localizer.ForPlayer(player, "sanction.permanent")
                : Localizer.ForPlayer(player, "sanction.until", until)
        );
    }

    private static List<CCSPlayerController> Humans()
    {
        return Utilities.GetPlayers().Where(IsHuman).ToList();
    }

    private static bool IsHuman(CCSPlayerController player)
    {
        return player.IsValid && !player.IsBot && !player.IsHLTV && SteamIdOf(player) != 0;
    }

    // Before Steam verifies a player only the id they claim is known. Enforcing
    // on it is safe because a sanction only ever takes something away: a player
    // who claims somebody else's id gains nothing, and can only inherit a ban.
    private static ulong SteamIdOf(CCSPlayerController player)
    {
        return player.AuthorizedSteamID?.SteamId64 ?? player.SteamID;
    }
}
