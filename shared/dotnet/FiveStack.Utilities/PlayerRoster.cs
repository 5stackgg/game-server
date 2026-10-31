using System.Net;
using FiveStack.Entities.PlayerManagement;

namespace FiveStack.Utilities;

public sealed record ObservedPlayer(string SteamId, string Name, string? Ip, bool Verified);

// What a death event says beyond who died, for the detailed stats. The killer's
// and victim's slots and teams are there only for a counted kill (a slot of -1
// otherwise), since a trade is told by slot. TeamKiller is the roster id of a
// player who killed a teammate, and Suicide is a death by one's own hand or the
// world's, as IsSuicide tells it. ByBot is a death at a bot's hands, which ends
// a streak and is no death.
public sealed record KillFacts(
    string Weapon = "",
    float Time = 0,
    int KillerSlot = -1,
    int KillerTeam = 0,
    int VictimSlot = -1,
    int VictimTeam = 0,
    bool TeammatesAreEnemies = false,
    bool NoScope = false,
    bool ThruSmoke = false,
    bool AttackerBlind = false,
    int Penetrated = 0,
    bool AttackerInAir = false,
    bool AssistedFlash = false,
    bool Suicide = false,
    string? TeamKiller = null,
    bool ByBot = false
);

// What a hurt event says beyond the damage, which the hooks pass only for a
// human victim and an attacker who is no bot. Enemy is a hit by the rule a
// kill counts by; Team is a human hurting a human, themselves included, who is
// no enemy, which counts for nothing where teammates are enemies. The tick
// tells a shotgun's pellets apart from separate shots.
public sealed record HitFacts(
    string Weapon,
    int HitGroup,
    int Tick,
    int VictimSlot,
    bool Enemy,
    bool Team = false,
    bool TeammatesAreEnemies = false
);

public sealed record RosterReport(
    List<RosterPlayer>? Players,
    List<DepartedPlayer> Departed,
    string? Map
);

// Who is on the server, as the game thread last saw it, never as the connect
// and disconnect hooks tell it: a changelevel puts every client through a
// reconnect, and a disconnect hook can still list the leaving player or fail
// to resolve them at all.
public class PlayerRoster
{
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1.5);

    // A hibernating server stops ticking, and it only hibernates once it is
    // empty, so a roster that has not been observed for this long is empty.
    public static readonly TimeSpan Silence = TimeSpan.FromSeconds(5);

    // Clients drift back in over the first minute of a new map, and a player
    // still loading would otherwise be reported as having left.
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(45);

    public static readonly TimeSpan HoldCap = TimeSpan.FromMinutes(3);

    public static readonly TimeSpan DepartedTtl = TimeSpan.FromMinutes(10);

    public const int MaxDepartures = 64;

    public const int MaxNameLength = 64;

    // More than any one connection can use, so a server that names its own
    // weapons cannot grow a report without end.
    public const int MaxWeapons = 48;

    public const int TeamT = 2;

    public const int TeamCT = 3;

    // A player who has not been hit since they spawned.
    public const int FullHealth = 100;

    private enum eRosterState
    {
        Unknown,
        Hold,
        Known,
    }

    private sealed class Entry
    {
        public readonly string Conn = Guid.NewGuid().ToString("N");
        public string Name = "";
        public string? Ip;
        public int Kills;
        public int Deaths;
        public int Assists;
        public int Headshots;
        public int Damage;
        public int RoundsPlayed;
        public int RoundsWon;
        public int Mvps;
        public readonly Dictionary<string, int> Stats = new();
        public readonly Dictionary<string, WeaponTotals> Weapons = new();
        public int Streak;
        public int BestStreak;
        public int HitTick = -1;
        public int BotHitTick = -1;
        public readonly HashSet<int> HitSlots = new();

        public void Add(string key, int amount = 1)
        {
            Stats[key] = Stats.GetValueOrDefault(key) + amount;
        }

        // Null once the cap is reached, for a weapon the connection has not
        // used before.
        public WeaponTotals? Weapon(string weapon)
        {
            string name = CommunityWeapons.Normalize(weapon);

            if (name.Length == 0)
            {
                return null;
            }

            if (!Weapons.TryGetValue(name, out WeaponTotals? totals))
            {
                if (Weapons.Count >= MaxWeapons)
                {
                    return null;
                }

                totals = new WeaponTotals();
                Weapons[name] = totals;
            }

            return totals;
        }
    }

    private sealed record Departure(DepartedPlayer Player, DateTimeOffset At);

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _current = new();
    private readonly List<Departure> _departed = new();
    private Dictionary<string, ObservedPlayer> _latest = new();
    private HashSet<string> _held = new();
    private Dictionary<string, string>? _sent;
    private eRosterState _state = eRosterState.Unknown;
    private DateTimeOffset? _observedAt;
    private DateTimeOffset? _worldAt;
    private DateTimeOffset? _dueAt;
    private DateTimeOffset _holdSince;
    private bool _mapStarted;
    private DateTimeOffset? _releaseAt;
    private string? _map;
    private RosterReport? _final;
    private DateTimeOffset _finalAt;
    private readonly CommunityTrades _trades = new();

    // Returns when a sync is due if this observation is what made one due.
    public virtual DateTimeOffset? Observe(IEnumerable<ObservedPlayer> players, DateTimeOffset now)
    {
        Dictionary<string, ObservedPlayer> verified = new();

        foreach (ObservedPlayer player in players)
        {
            if (player.Verified)
            {
                verified.TryAdd(player.SteamId, player);
            }
        }

        lock (_lock)
        {
            DateTimeOffset? before = _dueAt;

            _observedAt = now;
            _latest = verified;

            if (_state == eRosterState.Hold)
            {
                if (_mapStarted)
                {
                    _releaseAt ??= now + Settle;
                }

                Admit();
            }
            else
            {
                _state = eRosterState.Known;
                Reconcile(now);
            }

            Advance(now);

            return before == null ? _dueAt : null;
        }
    }

    // Ticks stop both while a server hibernates and while it loads a map, and
    // SwiftlyS2 only announces a map change once the load is over. A world that
    // keeps updating without ticking is hibernating; one that has stopped as
    // well is loading.
    public void WorldUpdated(DateTimeOffset now)
    {
        lock (_lock)
        {
            _worldAt = now;
        }
    }

    public bool IsHibernating(DateTimeOffset now)
    {
        lock (_lock)
        {
            return _worldAt != null
                && now - _worldAt <= Silence
                && (_observedAt == null || _worldAt - _observedAt > Silence);
        }
    }

    // The roster as the map ended goes out on its own, before the hold, so the
    // panel credits the map's last kills to it and not to the next map. A map
    // that ends before its own hold has settled has no roster to send, and a
    // map change while the last map's report is still undelivered replaces
    // it: either way those kills land on the next map.
    public void MapEnded(DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_state == eRosterState.Known)
            {
                _final = new RosterReport(Players(), Departures(), _map);
                _finalAt = now;
                _dueAt = now;
            }

            Hold(now);
            _mapStarted = false;
            _releaseAt = null;
        }
    }

    public void MapStarted(DateTimeOffset now, string? map)
    {
        lock (_lock)
        {
            _map = MapName(map);
            Hold(now);
            _mapStarted = true;
            _releaseAt = null;
            _trades.Clear();

            // A killstreak is a map's, like the match stats' best streak.
            foreach (Entry entry in _current.Values)
            {
                entry.Streak = 0;
                entry.BestStreak = 0;
            }
        }
    }

    // For a plugin loaded onto a map already running, which never sees it start.
    public void MapIs(string? map)
    {
        lock (_lock)
        {
            _map = MapName(map);
        }
    }

    // Trusted even through a hold: the server only hibernates once nobody is
    // connected, and the ticks that would settle the hold have stopped.
    public void Hibernating(DateTimeOffset now)
    {
        lock (_lock)
        {
            _state = eRosterState.Known;
            _latest = new();
            Reconcile(now);
            Advance(now);
        }
    }

    // Only players on the roster are counted, which keeps out bots and any
    // claimed id Steam has not verified. The hooks pass a killer or assister
    // only when the kill or assist counts, so a headshot only ever goes with a
    // counted kill. Nothing done in warmup counts: its kills, deaths, damage
    // and MVPs no more than its rounds. The game rules' warmup flag says so at
    // each event, however long the warmup runs or however it ends. An assist
    // whose facts say it was a flash's is a flash assist and not an assist.
    public void Died(
        string? victim,
        string? killer,
        bool warmup,
        string? assister = null,
        bool headshot = false,
        KillFacts? facts = null
    )
    {
        if (warmup)
        {
            return;
        }

        lock (_lock)
        {
            if (victim != null && _current.TryGetValue(victim, out Entry? dead))
            {
                dead.Streak = 0;

                if (facts?.ByBot != true)
                {
                    dead.Deaths++;

                    if (facts?.Suicide == true)
                    {
                        dead.Add(CommunityStatKeys.Suicides);
                    }
                }
            }

            if (
                facts?.TeamKiller != null
                && _current.TryGetValue(facts.TeamKiller, out Entry? traitor)
            )
            {
                traitor.Add(CommunityStatKeys.TeamKills);
            }

            if (killer != null && _current.TryGetValue(killer, out Entry? scorer))
            {
                scorer.Kills++;
                scorer.Streak++;
                scorer.BestStreak = Math.Max(scorer.BestStreak, scorer.Streak);

                if (headshot)
                {
                    scorer.Headshots++;
                }

                if (facts != null)
                {
                    Scored(scorer, facts, headshot);
                }
            }

            // Every counted kill is kept, whoever made it, since a kill by an
            // unverified player can still be traded by one on the roster. Any
            // other death still ends what the victim can be traded for.
            if (facts != null && facts.KillerSlot >= 0 && !facts.TeammatesAreEnemies)
            {
                int traded = _trades.Killed(
                    facts.KillerSlot,
                    facts.KillerTeam,
                    facts.VictimSlot,
                    facts.VictimTeam,
                    facts.Time
                );

                if (traded > 0 && killer != null && _current.TryGetValue(killer, out Entry? trader))
                {
                    trader.Add(CommunityStatKeys.TradeKills, traded);
                }
            }
            else if (facts != null && facts.VictimSlot >= 0)
            {
                _trades.Died(facts.VictimSlot);
            }

            if (
                assister != null
                && assister != killer
                && _current.TryGetValue(assister, out Entry? helper)
            )
            {
                if (facts?.AssistedFlash == true)
                {
                    helper.Add(CommunityStatKeys.FlashAssists);
                }
                else
                {
                    helper.Assists++;
                }
            }
        }
    }

    // The damage comes already capped by Dealt. Without facts the hooks pass
    // an attacker only for damage to an enemy, by the same rule as a kill.
    // With them, a hit on an enemy counts; a human's hurting a human who is no
    // enemy, themselves included, is team damage, as the match stats count
    // it, but not where teammates are enemies. Nothing to or by a bot counts.
    public void Hurt(
        string? attacker,
        string? victim,
        int damage,
        bool warmup,
        HitFacts? hit = null
    )
    {
        if (warmup)
        {
            return;
        }

        if (hit == null)
        {
            if (attacker == null || attacker == victim || damage <= 0)
            {
                return;
            }

            lock (_lock)
            {
                if (_current.TryGetValue(attacker, out Entry? entry))
                {
                    entry.Damage += damage;
                }
            }

            return;
        }

        lock (_lock)
        {
            if (attacker != null && _current.TryGetValue(attacker, out Entry? entry))
            {
                if (hit.Enemy)
                {
                    Hit(entry, hit, damage);
                }
                else if (hit.Team && !hit.TeammatesAreEnemies && damage > 0)
                {
                    entry.Add(CommunityStatKeys.TeamDamage, damage);
                }
            }

            if (
                hit.Enemy
                && damage > 0
                && victim != null
                && _current.TryGetValue(victim, out Entry? hurt)
            )
            {
                hurt.Add(CommunityStatKeys.DamageTaken, damage);
            }
        }
    }

    // Only a flash that blinds for more than a second counts, as the match
    // stats count it, and blinding oneself is blinding a teammate, except where
    // teammates are enemies and there are no team stats. The hooks pass only
    // human victims.
    public void Blinded(
        string? attacker,
        string? victim,
        float seconds,
        bool sameTeam,
        bool warmup,
        bool teammatesAreEnemies = false
    )
    {
        if (warmup || attacker == null || seconds <= 1.0f)
        {
            return;
        }

        lock (_lock)
        {
            if (!_current.TryGetValue(attacker, out Entry? entry))
            {
                return;
            }

            if (sameTeam || attacker == victim)
            {
                if (!teammatesAreEnemies)
                {
                    entry.Add(CommunityStatKeys.TeamFlashed);
                }

                return;
            }

            entry.Add(CommunityStatKeys.EnemiesFlashed);
            entry.Add(CommunityStatKeys.FlashDurationMs, (int)Math.Round(seconds * 1000));
        }
    }

    // A shot is a bullet weapon's: a knife's swing or a grenade's throw is not.
    public void Fired(string? steamId, string? weapon, bool warmup)
    {
        if (warmup || steamId == null || !CommunityWeapons.IsFirearm(weapon))
        {
            return;
        }

        lock (_lock)
        {
            if (_current.TryGetValue(steamId, out Entry? entry))
            {
                entry.Add(CommunityStatKeys.Shots);

                WeaponTotals? totals = entry.Weapon(weapon!);

                if (totals != null)
                {
                    totals.shots++;
                }
            }
        }
    }

    // Nothing aimed at a bot counts, so a shot that hits one is taken back; a
    // miss names no target and stays a shot. A shotgun's pellets in one tick
    // are one shot, and one that also hits a human stays a shot (see Hit).
    public void HitABot(string? attacker, string? weapon, int tick, bool warmup)
    {
        if (warmup || attacker == null || !CommunityWeapons.IsFirearm(weapon))
        {
            return;
        }

        lock (_lock)
        {
            if (
                !_current.TryGetValue(attacker, out Entry? entry)
                || entry.HitTick == tick
                || entry.BotHitTick == tick
            )
            {
                return;
            }

            WeaponTotals? totals = entry.Weapon(weapon!);

            // Never below nothing, for a shot fired before it could count.
            if (totals == null || totals.shots == 0)
            {
                return;
            }

            totals.shots--;
            entry.Add(CommunityStatKeys.Shots, -1);
            entry.BotHitTick = tick;
        }
    }

    // A bomb planted or defused, or a hostage rescued, by the stat's key.
    public void Objective(string? steamId, string key, bool warmup)
    {
        if (warmup || steamId == null)
        {
            return;
        }

        lock (_lock)
        {
            if (_current.TryGetValue(steamId, out Entry? entry))
            {
                entry.Add(key);
            }
        }
    }

    // A trade is a round's.
    public void RoundStarted()
    {
        lock (_lock)
        {
            _trades.Clear();
        }
    }

    // A round is played by whoever is on a side when it ends, so a player who
    // switches sides mid-round plays it for the side they ended it on, and a
    // spectator plays none. Warmup rounds are not rounds.
    public void RoundEnded(
        int winnerTeam,
        IReadOnlyDictionary<string, int> teamsBySteamId,
        bool warmup
    )
    {
        if (warmup)
        {
            return;
        }

        lock (_lock)
        {
            foreach ((string steamId, int team) in teamsBySteamId)
            {
                if (
                    (team != TeamT && team != TeamCT)
                    || !_current.TryGetValue(steamId, out Entry? entry)
                )
                {
                    continue;
                }

                entry.RoundsPlayed++;

                if (team == winnerTeam)
                {
                    entry.RoundsWon++;
                }
            }
        }
    }

    public void Mvp(string? steamId, bool warmup)
    {
        if (warmup)
        {
            return;
        }

        lock (_lock)
        {
            if (steamId != null && _current.TryGetValue(steamId, out Entry? entry))
            {
                entry.Mvps++;
            }
        }
    }

    public DateTimeOffset? DueAt(DateTimeOffset now)
    {
        lock (_lock)
        {
            Advance(now);

            return _dueAt;
        }
    }

    public virtual RosterReport Report(DateTimeOffset now)
    {
        lock (_lock)
        {
            Advance(now);

            _dueAt = null;

            if (_final != null)
            {
                _sent = _final.Players!.ToDictionary(player => player.conn, player => player.name);

                return _final;
            }

            List<RosterPlayer>? players = _state == eRosterState.Known ? Players() : null;

            _sent = players?.ToDictionary(player => player.conn, player => player.name);

            return new RosterReport(players, Departures(), _map);
        }
    }

    // A panel told the roster is unknown may ignore the departures that came
    // with it, and one that could not record them says so. A panel that says
    // nothing predates the roster and has taken them.
    public void Delivered(RosterReport report, bool? recorded)
    {
        if (report.Players == null || recorded == false)
        {
            return;
        }

        HashSet<string> conns = report.Departed.Select(departure => departure.conn).ToHashSet();

        lock (_lock)
        {
            if (ReferenceEquals(report, _final))
            {
                _final = null;
            }

            _departed.RemoveAll(departure => conns.Contains(departure.Player.conn));
        }
    }

    public static string Name(string? raw)
    {
        string name = ChatUtility.StripFormatting(raw ?? "").Trim();

        if (name.Length <= MaxNameLength)
        {
            return name;
        }

        int cut = char.IsHighSurrogate(name[MaxNameLength - 1]) ? MaxNameLength - 1 : MaxNameLength;

        return name[..cut].TrimEnd();
    }

    public static string? Address(string? raw)
    {
        if (
            string.IsNullOrWhiteSpace(raw)
            || !IPEndPoint.TryParse(raw.Trim(), out IPEndPoint? endpoint)
        )
        {
            return null;
        }

        IPAddress address = endpoint.Address.IsIPv4MappedToIPv6
            ? endpoint.Address.MapToIPv4()
            : endpoint.Address;

        if (
            IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
        )
        {
            return null;
        }

        return address.ToString();
    }

    // A connected controller is on the server whether or not it has a pawn.
    public static bool IsConnectedHuman(bool isHltv, bool connected, bool isBot, ulong steamId)
    {
        return !isHltv && connected && !isBot && steamId != 0;
    }

    // Identified by slot, not Steam id: an unverified victim can claim the
    // attacker's id.
    public static bool CountsAsKill(
        int attackerSlot,
        int victimSlot,
        int attackerTeam,
        int victimTeam,
        bool teammatesAreEnemies
    )
    {
        return attackerSlot != victimSlot && (teammatesAreEnemies || attackerTeam != victimTeam);
    }

    // Killing oneself is a suicide, not a team kill, and where teammates are
    // enemies nobody is a teammate.
    public static bool IsTeamKill(
        int attackerSlot,
        int victimSlot,
        int attackerTeam,
        int victimTeam,
        bool teammatesAreEnemies
    )
    {
        return attackerSlot != victimSlot && !teammatesAreEnemies && attackerTeam == victimTeam;
    }

    // No attacker is the world's doing, but the bomb has no attacker either,
    // and dying to it is no suicide.
    public static bool IsSuicide(int? attackerSlot, int victimSlot, string? weapon)
    {
        if (attackerSlot == null)
        {
            return CommunityWeapons.Normalize(weapon) != "planted_c4";
        }

        return attackerSlot == victimSlot;
    }

    // A flash assist only blinded the victim, which did nothing to their health.
    public static bool CountsAsAssist(
        int assisterSlot,
        int victimSlot,
        int assisterTeam,
        int victimTeam,
        bool teammatesAreEnemies,
        bool flash
    )
    {
        return !flash
            && CountsAsKill(
                assisterSlot,
                victimSlot,
                assisterTeam,
                victimTeam,
                teammatesAreEnemies
            );
    }

    // A hit reports its full damage even when it kills, so a lethal one is
    // capped at the health the victim had before it: what the last hit they
    // survived left them, or full health when they have not been hit since
    // spawning.
    public static int Dealt(int damage, int healthAfter, int? healthBefore)
    {
        int before = healthAfter > 0 ? healthAfter + damage : healthBefore ?? FullHealth;

        return Math.Clamp(damage, 0, Math.Max(before, 0));
    }

    private static void Scored(Entry scorer, KillFacts facts, bool headshot)
    {
        string? type = CommunityWeapons.KillType(facts.Weapon);

        if (type != null)
        {
            scorer.Add(type);
        }

        if (facts.NoScope)
        {
            scorer.Add(CommunityStatKeys.NoScopeKills);
        }

        if (facts.ThruSmoke)
        {
            scorer.Add(CommunityStatKeys.ThruSmokeKills);
        }

        if (facts.Penetrated > 0)
        {
            scorer.Add(CommunityStatKeys.ThruWallKills);
        }

        if (facts.AttackerBlind)
        {
            scorer.Add(CommunityStatKeys.BlindedKills);
        }

        if (facts.AttackerInAir)
        {
            scorer.Add(CommunityStatKeys.InAirKills);
        }

        WeaponTotals? totals = scorer.Weapon(facts.Weapon);

        if (totals != null)
        {
            totals.kills++;

            if (headshot)
            {
                totals.headshots++;
            }
        }
    }

    // A shotgun's pellets all hit in the same tick, and each victim they hit
    // is hit once, as the match stats' hits are shots that hit.
    private static void Hit(Entry entry, HitFacts hit, int damage)
    {
        WeaponTotals? totals = null;

        if (CommunityWeapons.IsFirearm(hit.Weapon))
        {
            if (entry.HitTick != hit.Tick)
            {
                entry.HitTick = hit.Tick;
                entry.HitSlots.Clear();

                // The same pellets hit a bot first, which took the shot back.
                if (entry.BotHitTick == hit.Tick)
                {
                    entry.BotHitTick = -1;
                    entry.Add(CommunityStatKeys.Shots);

                    WeaponTotals? shot = entry.Weapon(hit.Weapon);

                    if (shot != null)
                    {
                        shot.shots++;
                    }
                }
            }

            if (entry.HitSlots.Add(hit.VictimSlot))
            {
                entry.Add(CommunityStatKeys.Hits);

                string? group = CommunityStatKeys.HitGroup(hit.HitGroup);

                if (group != null)
                {
                    entry.Add(group);
                }

                totals = entry.Weapon(hit.Weapon);

                if (totals != null)
                {
                    totals.hits++;
                }
            }
        }

        if (damage <= 0)
        {
            return;
        }

        entry.Damage += damage;

        string? type = CommunityWeapons.DamageType(hit.Weapon);

        if (type != null)
        {
            entry.Add(type, damage);
        }

        totals ??= entry.Weapon(hit.Weapon);

        if (totals != null)
        {
            totals.damage += damage;
        }
    }

    // Copies, since the report is serialized outside the lock, with zeros
    // left out and nothing at all when nothing is left.
    private static Dictionary<string, int>? StatsOf(Entry entry)
    {
        Dictionary<string, int> stats = entry
            .Stats.Where(stat => stat.Value != 0)
            .ToDictionary(stat => stat.Key, stat => stat.Value);

        if (entry.BestStreak > 0)
        {
            stats[CommunityStatKeys.BestStreak] = entry.BestStreak;
        }

        return stats.Count == 0 ? null : stats;
    }

    private static Dictionary<string, WeaponTotals>? WeaponsOf(Entry entry)
    {
        Dictionary<string, WeaponTotals> weapons = entry
            .Weapons.Where(weapon =>
                weapon.Value.kills != 0
                || weapon.Value.headshots != 0
                || weapon.Value.shots != 0
                || weapon.Value.hits != 0
                || weapon.Value.damage != 0
            )
            .ToDictionary(weapon => weapon.Key, weapon => Copy(weapon.Value));

        return weapons.Count == 0 ? null : weapons;
    }

    private static Dictionary<string, WeaponTotals>? Copy(Dictionary<string, WeaponTotals>? weapons)
    {
        return weapons?.ToDictionary(weapon => weapon.Key, weapon => Copy(weapon.Value));
    }

    private static WeaponTotals Copy(WeaponTotals totals)
    {
        return new WeaponTotals
        {
            kills = totals.kills,
            headshots = totals.headshots,
            shots = totals.shots,
            hits = totals.hits,
            damage = totals.damage,
        };
    }

    // The panel checks the name itself; this only keeps an empty one from
    // reading as a map.
    private static string? MapName(string? raw)
    {
        string map = (raw ?? "").Trim();

        return map.Length == 0 ? null : map;
    }

    private List<RosterPlayer> Players()
    {
        return _current
            .Select(entry => new RosterPlayer
            {
                steam_id = entry.Key,
                conn = entry.Value.Conn,
                name = entry.Value.Name,
                ip = entry.Value.Ip,
                kills = entry.Value.Kills,
                deaths = entry.Value.Deaths,
                assists = entry.Value.Assists,
                headshots = entry.Value.Headshots,
                damage = entry.Value.Damage,
                rounds_played = entry.Value.RoundsPlayed,
                rounds_won = entry.Value.RoundsWon,
                mvps = entry.Value.Mvps,
                stats = StatsOf(entry.Value),
                weapons = WeaponsOf(entry.Value),
            })
            .ToList();
    }

    private List<DepartedPlayer> Departures()
    {
        return _departed
            .Take(MaxDepartures)
            .Select(departure => new DepartedPlayer
            {
                steam_id = departure.Player.steam_id,
                conn = departure.Player.conn,
                kills = departure.Player.kills,
                deaths = departure.Player.deaths,
                assists = departure.Player.assists,
                headshots = departure.Player.headshots,
                damage = departure.Player.damage,
                rounds_played = departure.Player.rounds_played,
                rounds_won = departure.Player.rounds_won,
                mvps = departure.Player.mvps,
                stats = departure.Player.stats == null ? null : new(departure.Player.stats),
                weapons = Copy(departure.Player.weapons),
            })
            .ToList();
    }

    private void Hold(DateTimeOffset now)
    {
        if (_state != eRosterState.Hold)
        {
            _state = eRosterState.Hold;
            _holdSince = now;
            _held = _current.Keys.ToHashSet();
        }
    }

    private void Advance(DateTimeOffset now)
    {
        _departed.RemoveAll(departure => now - departure.At > DepartedTtl);

        // A panel that cannot take the final report for this long is not
        // holding up every report after it.
        if (_final != null && now - _finalAt >= HoldCap)
        {
            _final = null;
        }

        if (_state == eRosterState.Hold && Settled(now))
        {
            _state = eRosterState.Known;
            Reconcile(now);
        }

        if (_state != eRosterState.Known)
        {
            return;
        }

        if (_current.Count > 0 && Silent(now))
        {
            _latest = new();
            Reconcile(now);
        }

        // A pending final report is what any sync would send, so a roster that
        // differs from it waits for it to be delivered or given up.
        if (_final == null && !Matches())
        {
            _dueAt ??= now + Debounce;
        }
    }

    // Only an observation from the new map counts: everyone held was on the
    // old one.
    private bool Settled(DateTimeOffset now)
    {
        if (now - _holdSince >= HoldCap)
        {
            return true;
        }

        return _releaseAt != null && (now >= _releaseAt || _held.All(_latest.ContainsKey));
    }

    private bool Silent(DateTimeOffset now)
    {
        if (_observedAt == null)
        {
            return true;
        }

        if (_worldAt == null)
        {
            return now - _observedAt > Silence;
        }

        return now - _observedAt > HoldCap || _worldAt - _observedAt > Silence;
    }

    private void Reconcile(DateTimeOffset now)
    {
        foreach (string gone in _current.Keys.Where(id => !_latest.ContainsKey(id)).ToList())
        {
            Entry entry = _current[gone];
            _current.Remove(gone);
            _departed.Add(
                new Departure(
                    new DepartedPlayer
                    {
                        steam_id = gone,
                        conn = entry.Conn,
                        kills = entry.Kills,
                        deaths = entry.Deaths,
                        assists = entry.Assists,
                        headshots = entry.Headshots,
                        damage = entry.Damage,
                        rounds_played = entry.RoundsPlayed,
                        rounds_won = entry.RoundsWon,
                        mvps = entry.Mvps,
                        stats = StatsOf(entry),
                        weapons = WeaponsOf(entry),
                    },
                    now
                )
            );
        }

        Admit();
    }

    // Through a hold players are only ever added: whoever has not made it back
    // yet is still loading the new map, not gone.
    private void Admit()
    {
        foreach (ObservedPlayer player in _latest.Values)
        {
            if (!_current.TryGetValue(player.SteamId, out Entry? entry))
            {
                entry = new Entry();
                _current[player.SteamId] = entry;
            }

            entry.Name = Name(player.Name);
            entry.Ip = Address(player.Ip) ?? entry.Ip;
        }
    }

    private bool Matches()
    {
        if (_sent == null || _sent.Count != _current.Count)
        {
            return false;
        }

        foreach (Entry entry in _current.Values)
        {
            if (!_sent.TryGetValue(entry.Conn, out string? name) || name != entry.Name)
            {
                return false;
            }
        }

        return true;
    }
}
