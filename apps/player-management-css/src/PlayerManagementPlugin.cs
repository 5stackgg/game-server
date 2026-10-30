using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.ValveConstants.Protobuf;
using FiveStack.Entities.PlayerManagement;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace PlayerManagement;

// Community servers only: a matchmaking server gets its sanctions on the match
// payload from the match plugin, and never loads this one.
[MinimumApiVersion(80)]
public partial class PlayerManagementPlugin : BasePlugin, IPluginConfig<PlayerManagementConfig>
{
    private const string Runtime = "counterstrikesharp";

    private const long EnforceEveryMs = 1000;

    private static readonly TimeSpan IdleReloadCheckEvery = TimeSpan.FromMinutes(1);

    public override string ModuleName => "PlayerManagement";
    public override string ModuleVersion => "__RELEASE_VERSION__";
    public override string ModuleAuthor => "5Stack.gg";
    public override string ModuleDescription =>
        "Enforces 5Stack bans, mutes, gags and access lists on community servers";

    public PlayerManagementConfig Config { get; set; } = new();

    private readonly SanctionBook _book = new();
    private readonly ServerAccessBook _access = new();
    private readonly PlayerRoster _roster = new();
    private SanctionSyncLoop? _loop;
    private volatile bool _hibernating;

    // What was last applied to each player present, so changes are announced
    // once, and whose mute bit this plugin set, so it only ever lifts its own
    // and never one an admin plugin on the same server set.
    private readonly Dictionary<ulong, SanctionState> _applied = new();
    private readonly HashSet<ulong> _mutedByUs = new();
    private readonly HashSet<ulong> _kicked = new();

    // Each player's health after the last hit they survived, by slot, so a
    // lethal hit is credited only with what it took. Cleared when they spawn.
    private readonly Dictionary<int, int> _health = new();

    private long _lastEnforceMs;

    private INetworkServerService? _networkServerService;
    private Timer? _idleReloadTimer;

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
                "player management is not configured; bans, mutes, gags and access lists are not enforced until API_DOMAIN, SERVER_ID and SERVER_API_PASSWORD are set"
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
            _access.Left(steamId.ToString());
            _applied.Remove(steamId);
            _mutedByUs.Remove(steamId);
            _kicked.Remove(steamId);
        });

        RegisterListener<Listeners.OnMapEnd>(() => _roster.MapEnded(DateTimeOffset.UtcNow));
        RegisterListener<Listeners.OnMapStart>(map =>
            _roster.MapStarted(DateTimeOffset.UtcNow, map)
        );

        RegisterListener<Listeners.OnServerHibernationUpdate>(hibernating =>
        {
            _hibernating = hibernating;

            if (hibernating)
            {
                _roster.Hibernating(DateTimeOffset.UtcNow);
            }
        });

        AddCommandListener("say", OnChat, HookMode.Pre);
        AddCommandListener("say_team", OnChat, HookMode.Pre);

        InstallConnectGate();
        LoadSteamRelay(hotReload);

        _loop = new SanctionSyncLoop(
            _book,
            _access,
            _roster,
            new SanctionsClient(),
            Config.Settings,
            ModuleVersion,
            Runtime,
            SteamRelay,
            () => _hibernating,
            message => Logger.LogWarning("{message}", message),
            message => Logger.LogInformation("{message}", message)
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
            Logger.LogDebug(error, "unable to read the players present on load");
        }

        // A hot reload has missed the map's load, and a cold boot has no map to
        // ask about yet: it arrives with OnMapStart.
        if (hotReload)
        {
            try
            {
                _roster.MapIs(Server.MapName);
            }
            catch (Exception error)
            {
                Logger.LogDebug(error, "unable to read the map on load");
            }
        }

        _loop.Start();

        // Queued onto a world update by a thread timer: an empty server
        // hibernates, and its frames and game timers stop while it does.
        _networkServerService = new INetworkServerService();
        _idleReloadTimer = new Timer(
            _ => Server.NextWorldUpdate(ReloadIdleMap),
            null,
            IdleReloadCheckEvery,
            IdleReloadCheckEvery
        );
    }

    public override void Unload(bool hotReload)
    {
        _idleReloadTimer?.Dispose();
        _idleReloadTimer = null;

        _loop?.Dispose();
        _loop = null;

        UninstallConnectGate();
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
                _access.Snapshot(),
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

            ObserveRoster(humans);
            Enforce(humans);
        }
        catch (Exception error)
        {
            Logger.LogError(error, "unable to enforce sanctions");
        }
    }

    private void ReloadIdleMap()
    {
        float curTime = Server.CurrentTime;

        if (!IdleMapReloadUtility.ShouldReload(curTime, Humans().Count, hasMatch: false))
        {
            return;
        }

        string command = IdleMapReloadUtility.ReloadCommand(
            Server.MapName,
            _networkServerService?.GetWorkshopID()
        );

        Logger.LogInformation(
            "empty server at curtime {curTime:0}s (hibernating: {hibernating}), reloading the map: {command}",
            curTime,
            _hibernating,
            command
        );
        Server.ExecuteCommand(command);
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

            // CounterStrikeSharp disconnects with a reason code only, so the
            // text goes to chat on the way out.
            if (_access.IsDenied(steamId.ToString()))
            {
                if (_kicked.Add(steamId))
                {
                    Logger.LogInformation(
                        "kicking {name} ({steamId}): not on the server's access list",
                        player.PlayerName,
                        steamId
                    );

                    player.PrintToChat(
                        _access.KickReason(Localizer.ForPlayer(player, "access.denied"))
                    );
                    player.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);
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

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        try
        {
            CCSPlayerController? victim = @event.Userid;

            if (victim == null || !IsHuman(victim))
            {
                return HookResult.Continue;
            }

            CCSPlayerController? attacker = @event.Attacker;
            CCSPlayerController? assister = @event.Assister;
            bool teammatesAreEnemies = TeammatesAreEnemies();

            bool kill =
                attacker != null
                && IsHuman(attacker)
                && PlayerRoster.CountsAsKill(
                    attacker.Slot,
                    victim.Slot,
                    attacker.TeamNum,
                    victim.TeamNum,
                    teammatesAreEnemies
                );

            bool assist =
                assister != null
                && IsHuman(assister)
                && PlayerRoster.CountsAsAssist(
                    assister.Slot,
                    victim.Slot,
                    assister.TeamNum,
                    victim.TeamNum,
                    teammatesAreEnemies,
                    @event.Assistedflash
                );

            _roster.Died(
                VerifiedSteamIdOf(victim),
                kill ? VerifiedSteamIdOf(attacker!) : null,
                IsWarmup(),
                assist ? VerifiedSteamIdOf(assister!) : null,
                @event.Headshot
            );
        }
        catch (Exception error)
        {
            Logger.LogError(error, "unable to count a death");
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        try
        {
            CCSPlayerController? victim = @event.Userid;

            if (victim == null || !IsHuman(victim))
            {
                return HookResult.Continue;
            }

            int dealt = PlayerRoster.Dealt(
                @event.DmgHealth,
                @event.Health,
                _health.TryGetValue(victim.Slot, out int before) ? before : null
            );
            _health[victim.Slot] = @event.Health;

            CCSPlayerController? attacker = @event.Attacker;

            if (
                attacker == null
                || !IsHuman(attacker)
                || !PlayerRoster.CountsAsKill(
                    attacker.Slot,
                    victim.Slot,
                    attacker.TeamNum,
                    victim.TeamNum,
                    TeammatesAreEnemies()
                )
            )
            {
                return HookResult.Continue;
            }

            _roster.Hurt(
                VerifiedSteamIdOf(attacker),
                VerifiedSteamIdOf(victim),
                dealt,
                IsWarmup()
            );
        }
        catch (Exception error)
        {
            Logger.LogError(error, "unable to count damage");
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        try
        {
            CCSPlayerController? player = @event.Userid;

            if (player != null)
            {
                _health.Remove(player.Slot);
            }
        }
        catch (Exception error)
        {
            Logger.LogError(error, "unable to reset a player's health");
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        try
        {
            // Fired when the game starts or restarts, not at the end of a
            // round anybody played.
            if (@event.Message == "#SFUI_Notice_Game_Commencing")
            {
                return HookResult.Continue;
            }

            Dictionary<string, int> teams = new();

            foreach (CCSPlayerController player in Humans())
            {
                string? steamId = VerifiedSteamIdOf(player);

                if (steamId != null)
                {
                    teams[steamId] = player.TeamNum;
                }
            }

            _roster.RoundEnded(@event.Winner, teams, IsWarmup());
        }
        catch (Exception error)
        {
            Logger.LogError(error, "unable to count a round");
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundMvp(EventRoundMvp @event, GameEventInfo info)
    {
        try
        {
            CCSPlayerController? player = @event.Userid;

            if (player != null && IsHuman(player))
            {
                _roster.Mvp(VerifiedSteamIdOf(player), IsWarmup());
            }
        }
        catch (Exception error)
        {
            Logger.LogError(error, "unable to count an MVP");
        }

        return HookResult.Continue;
    }

    // Kept apart from the enforcement that follows it on the tick, which must
    // run whatever happens to the roster.
    private void ObserveRoster(List<CCSPlayerController> humans)
    {
        try
        {
            _loop?.Observe(Observed(humans), DateTimeOffset.UtcNow);
        }
        catch (Exception error)
        {
            Logger.LogError(error, "unable to observe the players present");
        }
    }

    // Unreadable is taken as teams as usual, where a team kill earns nothing.
    private static bool TeammatesAreEnemies()
    {
        try
        {
            return ConVar.Find("mp_teammates_are_enemies")?.GetPrimitiveValue<bool>() ?? false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Unreadable is taken as live, as a server without warmup is.
    private static bool IsWarmup()
    {
        try
        {
            return Utilities
                    .FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                    .FirstOrDefault()
                    ?.GameRules?.WarmupPeriod
                ?? false;
        }
        catch (Exception)
        {
            return false;
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

    // A controller lingers through its disconnect, so only a connected one is
    // on the roster.
    private static List<ObservedPlayer> Observed(List<CCSPlayerController> humans)
    {
        return humans
            .Select(player =>
            {
                bool verified =
                    player.AuthorizedSteamID != null
                    && player.Connected == PlayerConnectedState.Connected;

                return new ObservedPlayer(
                    SteamIdOf(player).ToString(),
                    player.PlayerName,
                    verified ? player.IpAddress : null,
                    verified
                );
            })
            .ToList();
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

    // Unlike a sanction, a kill credited to a claimed id is something gained.
    private static string? VerifiedSteamIdOf(CCSPlayerController player)
    {
        return player.AuthorizedSteamID?.SteamId64.ToString();
    }
}
