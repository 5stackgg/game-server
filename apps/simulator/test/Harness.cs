using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Simulator;

public sealed class InstantPacer : IPacer
{
    public Task Wait(double gameSeconds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

public sealed class FakeSource : IMatchSource
{
    private readonly Queue<MatchFetch> _answers = new();
    private MatchFetch _last = new(eCurrentMatchAction.Clear);

    public int Fetches { get; private set; }

    public FakeSource Then(MatchFetch answer)
    {
        _answers.Enqueue(answer);
        return this;
    }

    public Task<MatchFetch> Fetch(CancellationToken token)
    {
        Fetches++;
        if (_answers.Count > 0)
        {
            _last = _answers.Dequeue();
        }

        return Task.FromResult(_last);
    }
}

public sealed record Published(string Name, Dictionary<string, object> Data);

public sealed class RecordingSink : IEventSink
{
    private readonly List<Published> _events = new();

    public Action<Published>? OnPublish { get; set; }

    public List<Published> Events
    {
        get
        {
            lock (_events)
            {
                return _events.ToList();
            }
        }
    }

    public void Publish(Guid matchId, Guid mapId, string name, Dictionary<string, object> data)
    {
        var published = new Published(name, new Dictionary<string, object>(data));
        lock (_events)
        {
            _events.Add(published);
        }

        OnPublish?.Invoke(published);
    }

    public List<string> Statuses()
    {
        return Events
            .Where(e => e.Name == "mapStatus")
            .Select(e => (string)e.Data["status"])
            .ToList();
    }
}

public static class Matches
{
    public static MatchData Build(
        int perSide = 5,
        bool knife = false,
        string ready = "Players",
        bool placeholders = false,
        string map = "de_inferno",
        bool overtime = true
    )
    {
        Guid lineup1 = Guid.NewGuid();
        Guid lineup2 = Guid.NewGuid();
        Guid mapId = Guid.NewGuid();

        List<MatchMember> Members(Guid lineup, int offset)
        {
            return Enumerable
                .Range(0, perSide)
                .Select(index => new MatchMember
                {
                    steam_id = placeholders ? null : $"76561198000{offset + index:000000}",
                    name = placeholders ? "" : $"player-{offset + index}",
                    placeholder_name = placeholders ? $"ringer-{offset + index}" : "",
                    match_lineup_id = lineup,
                })
                .ToList();
        }

        return new MatchData
        {
            id = Guid.NewGuid(),
            lineup_1_id = lineup1,
            lineup_2_id = lineup2,
            lineup_1 = new MatchLineUp
            {
                id = lineup1,
                name = "one",
                lineup_players = Members(lineup1, 0),
            },
            lineup_2 = new MatchLineUp
            {
                id = lineup2,
                name = "two",
                lineup_players = Members(lineup2, 100),
            },
            current_match_map_id = mapId,
            match_maps =
            [
                new MatchMap
                {
                    id = mapId,
                    map = new Map { name = map },
                    order = 1,
                    status = "Scheduled",
                    lineup_1_side = "CT",
                    lineup_2_side = "TERRORIST",
                },
            ],
            options = new MatchOptions
            {
                mr = 12,
                best_of = 1,
                type = perSide switch
                {
                    1 => "Duel",
                    2 => "Wingman",
                    _ => "Competitive",
                },
                knife_round = knife,
                overtime = overtime,
                ready_setting = ready,
                tv_delay = 115,
            },
        };
    }

    private static readonly string[] SeriesMaps =
    [
        "de_inferno",
        "de_dust2",
        "de_mirage",
        "de_nuke",
        "de_ancient",
    ];

    // A series as the veto hands it out: one map per slot, sides alternating.
    public static MatchData Series(
        int bestOf,
        string lineup1 = "one",
        string lineup2 = "two",
        bool knife = true
    )
    {
        MatchData match = Build(knife: knife);
        match.options.best_of = bestOf;
        match.lineup_1.name = lineup1;
        match.lineup_2.name = lineup2;
        match.match_maps = Enumerable
            .Range(1, bestOf)
            .Select(order => new MatchMap
            {
                id = Guid.NewGuid(),
                map = new Map { name = SeriesMaps[order - 1] },
                order = order,
                status = "Scheduled",
                lineup_1_side = order % 2 == 1 ? "CT" : "TERRORIST",
                lineup_2_side = order % 2 == 1 ? "TERRORIST" : "CT",
            })
            .ToArray();
        match.current_match_map_id = match.match_maps[0].id;

        return match;
    }

    public static MatchFetch Live(MatchData match)
    {
        return new MatchFetch(eCurrentMatchAction.Setup, match, "Live");
    }

    public static SimulatedServer Server(
        IMatchSource source,
        RecordingSink sink,
        int seed = 1,
        bool onGameNode = true,
        string startMap = "de_inferno"
    )
    {
        return new SimulatedServer(
            new SimulatorSettings { OnGameNode = onGameNode, StartMap = startMap },
            source,
            sink,
            new InstantPacer(),
            new Random(seed),
            CancellationToken.None
        );
    }

    public static async Task Finish(MapRun? run)
    {
        if (run == null)
        {
            throw new InvalidOperationException("no map is being played");
        }

        await run.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}

// The api's side of a series: a map the server calls Finished moves the match
// on to its next map, and a decided series leaves the server with no match.
public sealed class SeriesApi : IMatchSource
{
    private readonly MatchData _match;
    private readonly int _toWin;

    public SeriesApi(MatchData match)
    {
        _match = match;
        _toWin = match.options.best_of / 2 + 1;
    }

    public List<Guid> MapWinners { get; } = new();
    public bool Over { get; private set; }

    public void Observe(Published published)
    {
        if (published.Name != "mapStatus" || (string)published.Data["status"] != "Finished")
        {
            return;
        }

        lock (MapWinners)
        {
            MatchMap map = _match.match_maps.First(map => map.id == _match.current_match_map_id);
            Guid winner = Guid.Parse((string)published.Data["winning_lineup_id"]);
            map.status = "Finished";
            map.winning_lineup_id = winner;
            MapWinners.Add(winner);

            if (MapWinners.GroupBy(id => id).Any(wins => wins.Count() >= _toWin))
            {
                Over = true;
                return;
            }

            _match.current_match_map_id = _match
                .match_maps.OrderBy(next => next.order)
                .First(next => next.status != "Finished")
                .id;
        }
    }

    public Task<MatchFetch> Fetch(CancellationToken token)
    {
        lock (MapWinners)
        {
            return Task.FromResult(
                Over ? new MatchFetch(eCurrentMatchAction.Clear) : Matches.Live(_match)
            );
        }
    }
}
