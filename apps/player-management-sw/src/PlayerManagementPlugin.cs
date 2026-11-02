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

    private const long IdleReloadCheckEveryMs = 60_000;

    private ILogger<PlayerManagementPlugin> _logger = null!;
    private IConfiguration _configuration = null!;
    private ServiceProvider? _serviceProvider;
    private SanctionSyncLoop? _loop;

    private readonly SanctionBook _book = new();
    private readonly ServerAccessBook _access = new();
    private readonly PlayerRoster _roster = new();

    // Looked up once a map rather than on every shot and hit.
    private CCSGameRules? _gameRules;

    // What was last applied to each player present, so changes are announced
    // once, and whose mute bit this plugin set, so it only ever lifts its own
    // and never one an admin plugin on the same server set.
    private readonly Dictionary<ulong, SanctionState> _applied = new();
    private readonly HashSet<ulong> _mutedByUs = new();
    private readonly HashSet<ulong> _kicked = new();

    private readonly HealthBook _health = new();

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
    private long _lastIdleReloadCheckMs;

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

        _mapUnloadHandler = _ =>
        {
            _gameRules = null;
            _roster.MapEnded(DateTimeOffset.UtcNow);
        };
        Core.Event.OnMapUnload += _mapUnloadHandler;

        _mapLoadHandler = @event =>
        {
            _gameRules = null;
            _roster.MapStarted(DateTimeOffset.UtcNow, @event.MapName);
        };
        Core.Event.OnMapLoad += _mapLoadHandler;

        _worldUpdateHandler = OnWorldUpdate;
        Core.Event.OnWorldUpdate += _worldUpdateHandler;

        _chatHookId = Core.Command.HookClientChat((playerId, text, teamonly) => OnChat(playerId));

        InstallConnectGate();
        LoadSteamRelay();

        _loop = new SanctionSyncLoop(
            _book,
            _access,
            _roster,
            new SanctionsClient(),
            Settings,
            ModuleVersion,
            Runtime,
            SteamRelay,
            () => _roster.IsHibernating(DateTimeOffset.UtcNow),
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

        // A hot reload has missed the map's load, and a cold boot has no map to
        // ask about yet: it arrives with OnMapLoad.
        if (hotReload)
        {
            try
            {
                _roster.MapIs(Core.Engine.GlobalVars.MapName.ToString());
            }
            catch (Exception error)
            {
                _logger.LogDebug(error, "unable to read the map on load");
            }

            // Nor has it seen the round under way go live, or anyone in it spawn.
            _roster.FreezeTimeEnded(IsWarmup());

            foreach (IPlayer player in Humans())
            {
                CountSpawn(player);
            }
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
            // First, so nothing below that fails leaves it stale.
            _roster.BotsPresent(Core.PlayerManager.GetAllPlayers().Any(IsBot));

            List<IPlayer> humans = Humans();

            ObserveRoster(humans);
            ReadHealth(humans);
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
            IPlayer? assister = @event.AssisterPlayer;
            bool teammatesAreEnemies = TeammatesAreEnemies();

            // Nothing a bot does counts, so a death at a bot's hands only
            // ends a streak and helping a bot to a kill is no assist.
            bool byBot = attacker != null && IsBot(attacker);

            bool kill =
                attacker != null
                && IsHuman(attacker)
                && PlayerRoster.CountsAsKill(
                    attacker.PlayerID,
                    victim.PlayerID,
                    attacker.Controller.TeamNum,
                    victim.Controller.TeamNum,
                    teammatesAreEnemies
                );

            bool assist =
                !byBot
                && assister != null
                && IsHuman(assister)
                && PlayerRoster.CountsAsAssist(
                    assister.PlayerID,
                    victim.PlayerID,
                    assister.Controller.TeamNum,
                    victim.Controller.TeamNum,
                    teammatesAreEnemies,
                    @event.AssistedFlash
                );

            // A flash assist is counted apart, by the same rule as a kill.
            bool flashAssist =
                @event.AssistedFlash
                && !byBot
                && assister != null
                && IsHuman(assister)
                && PlayerRoster.CountsAsKill(
                    assister.PlayerID,
                    victim.PlayerID,
                    assister.Controller.TeamNum,
                    victim.Controller.TeamNum,
                    teammatesAreEnemies
                );

            bool teamKill =
                attacker != null
                && IsHuman(attacker)
                && PlayerRoster.IsTeamKill(
                    attacker.PlayerID,
                    victim.PlayerID,
                    attacker.Controller.TeamNum,
                    victim.Controller.TeamNum,
                    teammatesAreEnemies
                );

            KillFacts facts = new(
                Weapon: kill ? HeldWeapon(attacker!, @event.Weapon) : @event.Weapon,
                Time: Core.Engine.GlobalVars.CurrentTime,
                KillerSlot: kill ? attacker!.PlayerID : -1,
                KillerTeam: kill ? attacker!.Controller.TeamNum : 0,
                VictimSlot: victim.PlayerID,
                VictimTeam: victim.Controller.TeamNum,
                TeammatesAreEnemies: teammatesAreEnemies,
                NoScope: @event.NoScope,
                ThruSmoke: @event.ThruSmoke,
                AttackerBlind: @event.AttackerBlind,
                Penetrated: @event.Penetrated,
                AttackerInAir: @event.AttackerInAir,
                AssistedFlash: flashAssist,
                Suicide: PlayerRoster.IsSuicide(
                    attacker != null && attacker.IsValid ? attacker.PlayerID : null,
                    victim.PlayerID,
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
            _logger.LogError(error, "unable to count a death");
        }

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnPlayerHurt(EventPlayerHurt @event)
    {
        try
        {
            IPlayer? victim = @event.UserIdPlayer;

            if (victim == null || !victim.IsValid)
            {
                return HookResult.Continue;
            }

            int dealt = _health.Hit(victim.PlayerID, @event.ActualDmgHealth, @event.ActualHealth);

            // Nothing a bot does or has done to it counts, nor anything
            // without an attacker. Only a human hurting a human who is no
            // enemy is team damage.
            IPlayer? attacker = @event.AttackerPlayer;

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
                attacker.PlayerID,
                victim.PlayerID,
                attacker.Controller.TeamNum,
                victim.Controller.TeamNum,
                teammatesAreEnemies
            );

            _roster.Hurt(
                attackerIsHuman ? VerifiedSteamIdOf(attacker) : null,
                VerifiedSteamIdOf(victim),
                dealt,
                IsWarmup(),
                new HitFacts(
                    weapon,
                    (int)@event.ActualHitGroup,
                    Core.Engine.GlobalVars.TickCount,
                    victim.PlayerID,
                    enemy,
                    attackerIsHuman && !enemy,
                    teammatesAreEnemies
                )
            );
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to count damage");
        }

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event)
    {
        try
        {
            IPlayer? player = @event.UserIdPlayer;

            if (player != null)
            {
                _health.Spawned(player.PlayerID);

                // Joining a side fires a spawn too, for a player who stays dead
                // until the next round, so whether they are alive is only read
                // once the spawn is over.
                Core.Scheduler.NextTick(() => CountSpawn(player));
            }
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to reset a player's health");
        }

        return HookResult.Continue;
    }

    private void CountSpawn(IPlayer player)
    {
        try
        {
            if (IsHuman(player) && player.IsAlive)
            {
                _roster.Spawned(VerifiedSteamIdOf(player), player.Controller.TeamNum);
            }
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to count a spawn");
        }
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnWeaponFire(EventWeaponFire @event)
    {
        try
        {
            // This runs for every shot and every throw, so the cheap checks
            // come before the lookups.
            if (!CommunityWeapons.IsFirearm(@event.Weapon))
            {
                return HookResult.Continue;
            }

            IPlayer? player = @event.UserIdPlayer;

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
            _logger.LogError(error, "unable to count a shot");
        }

        return HookResult.Continue;
    }

    // Blinding oneself is blinding a teammate, as it is on the match side.
    [GameEventHandler(HookMode.Post)]
    public HookResult OnPlayerBlind(EventPlayerBlind @event)
    {
        try
        {
            IPlayer? victim = @event.UserIdPlayer;
            IPlayer? attacker = @event.AttackerPlayer;

            if (victim == null || !IsHuman(victim) || attacker == null || !IsHuman(attacker))
            {
                return HookResult.Continue;
            }

            // A spectator or a dead player can be blinded too, and counts for
            // nothing.
            int victimTeam = victim.Controller.TeamNum;

            if (
                (victimTeam != PlayerRoster.TeamT && victimTeam != PlayerRoster.TeamCT)
                || !victim.IsAlive
            )
            {
                return HookResult.Continue;
            }

            bool teammatesAreEnemies = TeammatesAreEnemies();

            bool sameTeam = !PlayerRoster.CountsAsKill(
                attacker.PlayerID,
                victim.PlayerID,
                attacker.Controller.TeamNum,
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
            _logger.LogError(error, "unable to count a flash");
        }

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnBombPlanted(EventBombPlanted @event)
    {
        Objective(@event.UserIdPlayer, CommunityStatKeys.BombPlants);

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnBombDefused(EventBombDefused @event)
    {
        Objective(@event.UserIdPlayer, CommunityStatKeys.BombDefuses);

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnHostageRescued(EventHostageRescued @event)
    {
        Objective(@event.UserIdPlayer, CommunityStatKeys.HostageRescues);

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnRoundStart(EventRoundStart @event)
    {
        try
        {
            _roster.RoundStarted();
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to start a round");
        }

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnRoundFreezeEnd(EventRoundFreezeEnd @event)
    {
        try
        {
            _roster.FreezeTimeEnded(IsWarmup());
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to start counting a round");
        }

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnRoundEnd(EventRoundEnd @event)
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

            foreach (IPlayer player in Humans())
            {
                string? steamId = VerifiedSteamIdOf(player);

                if (steamId != null)
                {
                    teams[steamId] = player.Controller.TeamNum;
                }
            }

            _roster.RoundEnded(@event.Winner, teams, IsWarmup());
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to count a round");
        }

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnRoundMvp(EventRoundMvp @event)
    {
        try
        {
            IPlayer? player = @event.UserIdPlayer;

            // A bot's MVP, or one whose player has gone, is still the round's
            // MVP, so it reaches the roster without a player to credit.
            _roster.Mvp(
                player != null && IsHuman(player) ? VerifiedSteamIdOf(player) : null,
                IsWarmup()
            );
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to count an MVP");
        }

        return HookResult.Continue;
    }

    private void Objective(IPlayer? player, string key)
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
            _logger.LogError(error, "unable to count an objective");
        }
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

    // Some guns share another's class (see CommunityWeapons), and an event
    // may then name the other gun: player_hurt names a USP-S hkp2000. Only
    // for those names is the gun in hand read, by its item definition index;
    // anything unreadable keeps the event's name.
    private static string HeldWeapon(IPlayer player, string weapon)
    {
        if (!CommunityWeapons.SharesAClass(weapon))
        {
            return weapon;
        }

        CBasePlayerWeapon? active = player.PlayerPawn?.WeaponServices?.ActiveWeapon.Value;

        return CommunityWeapons.InHand(
            weapon,
            active != null && active.IsValid
                ? active.AttributeManager.Item.ItemDefinitionIndex
                : null
        );
    }

    // Also kept apart from the enforcement. A pawn that cannot be read keeps
    // the health its last hit left.
    private void ReadHealth(List<IPlayer> humans)
    {
        foreach (IPlayer player in humans)
        {
            try
            {
                CCSPlayerPawn? pawn = player.PlayerPawn;

                if (pawn != null && pawn.IsValid)
                {
                    _health.Seen(player.PlayerID, pawn.Health);
                }
            }
            catch (Exception error)
            {
                _logger.LogDebug(error, "unable to read a player's health");
            }
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

    // Unreadable is taken as live, as a server without warmup is. The game
    // rules last as long as the map, so only the lookup is kept, and the flag
    // is read fresh each time.
    private bool IsWarmup()
    {
        try
        {
            _gameRules ??= Core
                .EntitySystem.GetAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
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

    // Still called while the server hibernates, when OnTick is not, which is
    // how the roster tells an empty server from one loading a map, and what
    // lets an empty server reload its map.
    private void OnWorldUpdate()
    {
        long nowMs = Environment.TickCount64;

        if (nowMs - _lastWorldUpdateMs < EnforceEveryMs)
        {
            return;
        }

        _lastWorldUpdateMs = nowMs;
        _roster.WorldUpdated(DateTimeOffset.UtcNow);

        if (nowMs - _lastIdleReloadCheckMs < IdleReloadCheckEveryMs)
        {
            return;
        }

        _lastIdleReloadCheckMs = nowMs;
        ReloadIdleMap();
    }

    // Every client counts, still loading or not: SwiftlyS2 lists a player from
    // the moment the engine accepts the connection, and only bots and CSTV are
    // fake clients.
    private void ReloadIdleMap()
    {
        float curTime = Core.Engine.GlobalVars.CurrentTime;
        int players = Core.PlayerManager.GetAllPlayers().Count(player => !player.IsFakeClient);

        if (!IdleMapReloadUtility.ShouldReload(curTime, players, hasMatch: false))
        {
            return;
        }

        string command = IdleMapReloadUtility.ReloadCommand(
            Core.Engine.GlobalVars.MapName.ToString(),
            Core.Engine.WorkshopId
        );

        _logger.LogInformation(
            "empty server at curtime {curTime:0}s (hibernating: {hibernating}), reloading the map: {command}",
            curTime,
            _roster.IsHibernating(DateTimeOffset.UtcNow),
            command
        );
        Core.Engine.ExecuteCommand(command);
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

    // CSTV is a fake client too, and no bot.
    private static bool IsBot(IPlayer player)
    {
        CCSPlayerController controller = player.Controller;

        return controller.IsValid && player.IsFakeClient && !controller.IsHLTV;
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
