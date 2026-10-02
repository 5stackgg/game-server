using System.Net;
using FiveStack.Entities.PlayerManagement;

namespace FiveStack.Utilities;

public sealed record ObservedPlayer(string SteamId, string Name, string? Ip, bool Verified);

public sealed record RosterReport(List<RosterPlayer>? Players, List<DepartedPlayer> Departed);

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

    public void MapEnded(DateTimeOffset now)
    {
        lock (_lock)
        {
            Hold(now);
            _mapStarted = false;
            _releaseAt = null;
        }
    }

    public void MapStarted(DateTimeOffset now)
    {
        lock (_lock)
        {
            Hold(now);
            _mapStarted = true;
            _releaseAt = null;
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
    // claimed id Steam has not verified.
    public void Died(string? victim, string? killer)
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

            List<RosterPlayer>? players =
                _state == eRosterState.Known
                    ? _current
                        .Select(entry => new RosterPlayer
                        {
                            steam_id = entry.Key,
                            conn = entry.Value.Conn,
                            name = entry.Value.Name,
                            ip = entry.Value.Ip,
                            kills = entry.Value.Kills,
                            deaths = entry.Value.Deaths,
                        })
                        .ToList()
                    : null;

            _sent = players?.ToDictionary(player => player.conn, player => player.name);
            _dueAt = null;

            List<DepartedPlayer> departed = _departed
                .Take(MaxDepartures)
                .Select(departure => new DepartedPlayer
                {
                    steam_id = departure.Player.steam_id,
                    conn = departure.Player.conn,
                    kills = departure.Player.kills,
                    deaths = departure.Player.deaths,
                })
                .ToList();

            return new RosterReport(players, departed);
        }
    }

    // A panel told the roster is unknown may ignore the departures that came
    // with it, and one that could not record them says so. A panel that says
    // nothing predates the roster and has taken them.
    public void Delivered(RosterReport report, bool? recorded)
    {
        if (report.Players == null || recorded == false || report.Departed.Count == 0)
        {
            return;
        }

        HashSet<string> conns = report.Departed.Select(departure => departure.conn).ToHashSet();

        lock (_lock)
        {
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

        if (!Matches())
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
