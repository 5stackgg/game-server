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

    // Looked up once a map rather than on every shot and hit.
    private CCSGameRules? _gameRules;
    private SanctionSyncLoop? _loop;
    private volatile bool _hibernating;

    // What was last applied to each player present, so changes are announced
    // once, and whose mute bit this plugin set, so it only ever lifts its own
    // and never one an admin plugin on the same server set.
    private readonly Dictionary<ulong, SanctionState> _applied = new();
    private readonly HashSet<ulong> _mutedByUs = new();
    private readonly HashSet<ulong> _kicked = new();

    private readonly HealthBook _health = new();

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

        RegisterListener<Listeners.OnMapEnd>(() =>
        {
            _gameRules = null;
            _roster.MapEnded(DateTimeOffset.UtcNow);
        });
        RegisterListener<Listeners.OnMapStart>(map =>
        {
            _gameRules = null;
            _roster.MapStarted(DateTimeOffset.UtcNow, map);
        });

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

            // Nor has it seen the round under way go live, or anyone in it spawn:
            // whoever is alive now is in it, but whoever already died in it is
            // not.
            _roster.FreezeTimeEnded(IsWarmup());

            foreach (CCSPlayerController player in Humans())
            {
                CountSpawn(player);
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
            // First, so nothing below that fails leaves it stale.
            _roster.BotsPresent(Utilities.GetPlayers().Any(IsBot));

            List<CCSPlayerController> humans = Humans();

            ObserveRoster(humans);
            ReadHealth(humans);
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

            // Nothing a bot does counts, so a death at a bot's hands only
            // ends a streak and helping a bot to a kill is no assist.
            bool byBot = attacker != null && IsBot(attacker);

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
                !byBot
                && assister != null
                && IsHuman(assister)
                && PlayerRoster.CountsAsAssist(
                    assister.Slot,
                    victim.Slot,
                    assister.TeamNum,
                    victim.TeamNum,
                    teammatesAreEnemies,
                    @event.Assistedflash
                );

            // A flash assist is counted apart, by the same rule as a kill.
            bool flashAssist =
                @event.Assistedflash
                && !byBot
                && assister != null
                && IsHuman(assister)
                && PlayerRoster.CountsAsKill(
                    assister.Slot,
                    victim.Slot,
                    assister.TeamNum,
                    victim.TeamNum,
                    teammatesAreEnemies
                );

            bool teamKill =
                attacker != null
                && IsHuman(attacker)
                && PlayerRoster.IsTeamKill(
                    attacker.Slot,
                    victim.Slot,
                    attacker.TeamNum,
                    victim.TeamNum,
                    teammatesAreEnemies
                );

            KillFacts facts = new(
                Weapon: kill ? HeldWeapon(attacker!, @event.Weapon) : @event.Weapon,
                Time: Server.CurrentTime,
                KillerSlot: kill ? attacker!.Slot : -1,
                KillerTeam: kill ? attacker!.TeamNum : 0,
                VictimSlot: victim.Slot,
                VictimTeam: victim.TeamNum,
                TeammatesAreEnemies: teammatesAreEnemies,
                NoScope: @event.Noscope,
                ThruSmoke: @event.Thrusmoke,
                AttackerBlind: @event.Attackerblind,
                Penetrated: @event.Penetrated,
                AttackerInAir: @event.Attackerinair,
                AssistedFlash: flashAssist,
                Suicide: PlayerRoster.IsSuicide(
                    attacker != null && attacker.IsValid ? attacker.Slot : null,
                    victim.Slot,
                    @event.Weapon
                ),
                TeamKiller: teamKill ? VerifiedSteamIdOf(attacker!) : null,
                ByBot: byBot
            );

            _roster.Died(
                VerifiedSteamIdOf(victim),
                kill ? VerifiedSteamIdOf(attacker!) : null,
                IsWarmup(),
                assist || flashAssist ? VerifiedSteamIdOf(assister!) : null,
                @event.Headshot,
                facts
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

            if (victim == null || !victim.IsValid)
            {
                return HookResult.Continue;
            }

            int dealt = _health.Hit(victim.Slot, @event.DmgHealth, @event.Health);

            // Nothing a bot does or has done to it counts, nor anything
            // without an attacker. Only a human hurting a human who is no
            // enemy is team damage.
            CCSPlayerController? attacker = @event.Attacker;

            if (attacker == null || !attacker.IsValid || IsBot(attacker))
            {
                return HookResult.Continue;
            }

            bool attackerIsHuman = IsHuman(attacker);
            string weapon = attackerIsHuman ? HeldWeapon(attacker, @event.Weapon) : @event.Weapon;

            if (!IsHuman(victim))
            {
                return HookResult.Continue;
            }

            bool teammatesAreEnemies = TeammatesAreEnemies();

            bool enemy = PlayerRoster.CountsAsKill(
                attacker.Slot,
                victim.Slot,
                attacker.TeamNum,
                victim.TeamNum,
                teammatesAreEnemies
            );

            _roster.Hurt(
                attackerIsHuman ? VerifiedSteamIdOf(attacker) : null,
                VerifiedSteamIdOf(victim),
                dealt,
                IsWarmup(),
                new HitFacts(
                    weapon,
                    @event.Hitgroup,
                    Server.TickCount,
                    victim.Slot,
                    enemy,
                    attackerIsHuman && !enemy,
                    teammatesAreEnemies
                )
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
                _health.Spawned(player.Slot);

                // Joining a side fires a spawn too, for a player who stays dead
                // until the next round, so whether they are alive is only read
                // once the spawn is over. A spawn in the frame the round ends
                // is read after it, and so goes to the next round.
                Server.NextFrame(() => CountSpawn(player));
            }
        }
        catch (Exception error)
        {
            Logger.LogError(error, "unable to reset a player's health or count their spawn");
        }

        return HookResult.Continue;
    }

    // Read off the player's own pawn rather than a flag the controller keeps
    // for it. A spawn is kept under the id the player claims, so one who spawns
    // before Steam verifies them is in the round; only a verified id is
    // credited for it at the round's end.
    private void CountSpawn(CCSPlayerController player)
    {
        try
        {
            if (
                IsHuman(player)
                && player.PlayerPawn.Value is { IsValid: true } pawn
                && pawn.LifeState == (byte)LifeState_t.LIFE_ALIVE
            )
            {
                _roster.Spawned(SteamIdOf(player).ToString(), player.TeamNum);
            }
        }
        catch (Exception error)
        {
            Logger.LogError(error, "unable to count a spawn");
        }
    }

    [GameEventHandler]
    public HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        try
        {
            CCSPlayerController? player = @event.Userid;

            if (player != null && IsHuman(player) && @event.Team != @event.Oldteam)
            {
                _roster.LeftSide(SteamIdOf(player).ToString());
            }
        }
        catch (Exception error)
        {
            Logger.LogError(error, "unable to forget a spawn on a team change");
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        try
        {
            // This runs for every shot and every throw, so the cheap checks
            // come before the lookups.
            if (!CommunityWeapons.IsFirearm(@event.Weapon))
            {
                return HookResult.Continue;
            }

            CCSPlayerController? player = @event.Userid;

            if (player == null || !IsHuman(player))
            {
                return HookResult.Continue;
            }

            string? steamId = VerifiedSteamIdOf(player);

            if (steamId == null)
            {
                return HookResult.Continue;
            }

            _roster.Fired(steamId, HeldWeapon(player, @event.Weapon), IsWarmup());
        }
        catch (Exception error)
        {
            Logger.LogError(error, "unable to count a shot");
        }

        return HookResult.Continue;
    }

    // Blinding oneself is blinding a teammate, as it is on the match side.
    [GameEventHandler]
    public HookResult OnPlayerBlind(EventPlayerBlind @event, GameEventInfo info)
    {
        try
        {
            CCSPlayerController? victim = @event.Userid;
            CCSPlayerController? attacker = @event.Attacker;

            if (victim == null || !IsHuman(victim) || attacker == null || !IsHuman(attacker))
            {
                return HookResult.Continue;
            }

            // A spectator or a dead player can be blinded too, and counts for
            // nothing.
            int victimTeam = victim.TeamNum;

            if (
                (victimTeam != PlayerRoster.TeamT && victimTeam != PlayerRoster.TeamCT)
                || !victim.PawnIsAlive
            )
            {
                return HookResult.Continue;
            }

            bool teammatesAreEnemies = TeammatesAreEnemies();

            bool sameTeam = !PlayerRoster.CountsAsKill(
                attacker.Slot,
                victim.Slot,
                attacker.TeamNum,
                victimTeam,
                teammatesAreEnemies
            );

            _roster.Blinded(
                VerifiedSteamIdOf(attacker),
                VerifiedSteamIdOf(victim),
                @event.BlindDuration,
                sameTeam,
                IsWarmup(),
                teammatesAreEnemies
            );
        }
        catch (Exception error)
        {
            Logger.LogError(error, "unable to count a flash");
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnBombPlanted(EventBombPlanted @event, GameEventInfo info)
    {
        Objective(@event.Userid, CommunityStatKeys.BombPlants);

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnBombDefused(EventBombDefused @event, GameEventInfo info)
    {
        Objective(@event.Userid, CommunityStatKeys.BombDefuses);

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnHostageRescued(EventHostageRescued @event, GameEventInfo info)
    {
        Objective(@event.Userid, CommunityStatKeys.HostageRescues);

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        try
        {
            _roster.RoundStarted();
        }
        catch (Exception error)
        {
            Logger.LogError(error, "unable to start a round");
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        try
        {
            _roster.FreezeTimeEnded(IsWarmup());
        }
        catch (Exception error)
        {
            Logger.LogError(error, "unable to start counting a round");
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
                _roster.GameCommencing();
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

            // A bot's MVP, or one whose player has gone, is still the round's
            // MVP, so it reaches the roster without a player to credit.
            _roster.Mvp(
                player != null && IsHuman(player) ? VerifiedSteamIdOf(player) : null,
                IsWarmup()
            );
        }
        catch (Exception error)
        {
            Logger.LogError(error, "unable to count an MVP");
        }

        return HookResult.Continue;
    }

    private void Objective(CCSPlayerController? player, string key)
    {
        try
        {
            if (player != null && IsHuman(player))
            {
                _roster.Objective(VerifiedSteamIdOf(player), key, IsWarmup());
            }
        }
        catch (Exception error)
        {
            Logger.LogError(error, "unable to count an objective");
        }
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

    // Some guns share another's class (see CommunityWeapons), and an event
    // may then name the other gun: player_hurt names a USP-S hkp2000. Only
    // for those names is the gun in hand read, by its item definition index;
    // anything unreadable keeps the event's name.
    private static string HeldWeapon(CCSPlayerController player, string weapon)
    {
        if (!CommunityWeapons.SharesAClass(weapon))
        {
            return weapon;
        }

        CBasePlayerWeapon? active = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;

        return CommunityWeapons.InHand(
            weapon,
            active != null && active.IsValid
                ? active.AttributeManager.Item.ItemDefinitionIndex
                : null
        );
    }

    // Also kept apart from the enforcement. A pawn that cannot be read keeps
    // the health its last hit left.
    private void ReadHealth(List<CCSPlayerController> humans)
    {
        foreach (CCSPlayerController player in humans)
        {
            try
            {
                CCSPlayerPawn? pawn = player.PlayerPawn.Value;

                if (pawn != null && pawn.IsValid)
                {
                    _health.Seen(player.Slot, pawn.Health);
                }
            }
            catch (Exception error)
            {
                Logger.LogDebug(error, "unable to read a player's health");
            }
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

    // Unreadable is taken as live, as a server without warmup is. The game
    // rules last as long as the map, so only the lookup is kept, and the flag
    // is read fresh each time.
    private bool IsWarmup()
    {
        try
        {
            _gameRules ??= Utilities
                .FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .FirstOrDefault()
                ?.GameRules;

            return _gameRules?.WarmupPeriod ?? false;
        }
        catch (Exception)
        {
            _gameRules = null;

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

    // CSTV is a fake client too, and no bot.
    private static bool IsBot(CCSPlayerController player)
    {
        return player.IsValid && player.IsBot && !player.IsHLTV;
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
