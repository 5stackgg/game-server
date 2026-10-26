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

    public static MatchFetch Live(MatchData match)
    {
        return new MatchFetch(eCurrentMatchAction.Setup, match, "Live");
    }

    public static SimulatedServer Server(
        FakeSource source,
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
