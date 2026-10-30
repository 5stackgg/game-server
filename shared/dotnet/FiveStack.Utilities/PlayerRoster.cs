using System.Net;
using FiveStack.Entities.PlayerManagement;

namespace FiveStack.Utilities;

public sealed record ObservedPlayer(string SteamId, string Name, string? Ip, bool Verified);

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
    // counted kill.
    public void Died(string? victim, string? killer, string? assister = null, bool headshot = false)
    {
        lock (_lock)
        {
            if (victim != null && _current.TryGetValue(victim, out Entry? dead))
            {
                dead.Deaths++;
            }

            if (killer != null && _current.TryGetValue(killer, out Entry? scorer))
            {
                scorer.Kills++;

                if (headshot)
                {
                    scorer.Headshots++;
                }
            }

            if (
                assister != null
                && assister != killer
                && _current.TryGetValue(assister, out Entry? helper)
            )
            {
                helper.Assists++;
            }
        }
    }

    // The hooks pass an attacker only for damage to an enemy, by the same rule
    // as a kill, and the damage already capped by Dealt.
    public void Hurt(string? attacker, string? victim, int damage)
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

    public void Mvp(string? steamId)
    {
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
