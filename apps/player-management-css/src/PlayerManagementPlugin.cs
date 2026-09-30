using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.ValveConstants.Protobuf;
using FiveStack.Entities.PlayerManagement;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace PlayerManagement;

// Community servers only: a matchmaking server gets its sanctions on the match
// payload from the match plugin, and never loads this one.
[MinimumApiVersion(80)]
public class PlayerManagementPlugin : BasePlugin, IPluginConfig<PlayerManagementConfig>
{
    private const string Runtime = "counterstrikesharp";

    private const long EnforceEveryMs = 1000;

    public override string ModuleName => "PlayerManagement";
    public override string ModuleVersion => "__RELEASE_VERSION__";
    public override string ModuleAuthor => "5Stack.gg";
    public override string ModuleDescription =>
        "Enforces 5Stack bans, mutes and gags on community servers";

    public PlayerManagementConfig Config { get; set; } = new();

    private readonly SanctionBook _book = new();
    private SanctionSyncLoop? _loop;

    // What was last applied to each player present, so changes are announced
    // once, and whose mute bit this plugin set, so it only ever lifts its own
    // and never one an admin plugin on the same server set.
    private readonly Dictionary<ulong, SanctionState> _applied = new();
    private readonly HashSet<ulong> _mutedByUs = new();
    private readonly HashSet<ulong> _kicked = new();

    private long _lastEnforceMs;

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

        RegisterListener<Listeners.OnTick>(OnTick);

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

            ulong steamId = SteamIdOf(player);

            _book.Left(steamId.ToString());
            _applied.Remove(steamId);
            _mutedByUs.Remove(steamId);
            _kicked.Remove(steamId);
        });

        AddCommandListener("say", OnChat, HookMode.Pre);
        AddCommandListener("say_team", OnChat, HookMode.Pre);

        _loop = new SanctionSyncLoop(
            _book,
            new SanctionsClient(),
            Config.Settings,
            ModuleVersion,
            Runtime,
            message => Logger.LogWarning("{message}", message),
            message => Logger.LogInformation("{message}", message)
        );
        _loop.Start();
    }

    public override void Unload(bool hotReload)
    {
        _loop?.Dispose();
        _loop = null;
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

        _loop?.Request();

        command.ReplyToCommand(PlayerManagementReport.Syncing(Humans().Count));
    }

    [ConsoleCommand("player_management_status", "Reports 5Stack player management state")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnStatus(CCSPlayerController? caller, CommandInfo command)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        (DateTimeOffset? lastSyncAt, string? lastError) = _loop?.Status() ?? (null, null);

        command.ReplyToCommand(
            PlayerManagementReport.Status(
                ModuleVersion,
                Runtime,
                Config.Settings(),
                lastSyncAt,
                lastError,
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

    // Only records the join: kicking a client from inside its own connect
    // callbacks is left to the next enforcement pass on the tick.
    private void OnJoined(int slot)
    {
        CCSPlayerController? player = Utilities.GetPlayerFromSlot(slot);

        if (player == null || player.IsBot || player.IsHLTV || SteamIdOf(player) == 0)
        {
            return;
        }

        _book.Joined(SteamIdOf(player).ToString());
        _loop?.Request();
    }

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
            List<CCSPlayerController> humans = Humans();

            _loop?.Observe(humans.Select(player => SteamIdOf(player).ToString()));

            Enforce(humans);
        }
        catch (Exception error)
        {
            Logger.LogError(error, "unable to enforce sanctions");
        }
    }

    private void Enforce(List<CCSPlayerController> humans)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        HashSet<ulong> present = humans.Select(SteamIdOf).ToHashSet();

        foreach (ulong gone in _applied.Keys.Where(steamId => !present.Contains(steamId)).ToList())
        {
            _applied.Remove(gone);
        }

        _mutedByUs.RemoveWhere(steamId => !present.Contains(steamId));
        _kicked.RemoveWhere(steamId => !present.Contains(steamId));

        foreach (CCSPlayerController player in humans)
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
            if (state.IsMuted && !player.VoiceFlags.HasFlag(VoiceFlags.Muted))
            {
                player.VoiceFlags |= VoiceFlags.Muted;
                _mutedByUs.Add(steamId);
            }
            else if (!state.IsMuted && _mutedByUs.Remove(steamId))
            {
                player.VoiceFlags &= ~VoiceFlags.Muted;
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
