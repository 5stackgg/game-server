using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FiveStack.Entities.PlayerManagement;
using FiveStack.Utilities;
using Xunit;

public class PlayerRosterTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed record Reported(string SteamId, string Name, string? Ip, int Kills, int Deaths);

    private sealed class Panel : HttpMessageHandler
    {
        public readonly List<JsonElement> Bodies = new();
        public Func<Task<HttpResponseMessage>> Answer = () => Task.FromResult(Ok());

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            using JsonDocument body = JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync(cancellationToken)
            );

            Bodies.Add(body.RootElement.Clone());

            return await Answer();
        }

        public static HttpResponseMessage Ok(bool? recorded = null)
        {
            string json =
                recorded == null
                    ? "{\"sanctions\":[]}"
                    : $"{{\"sanctions\":[],\"roster_recorded\":{(recorded.Value ? "true" : "false")}}}";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }

        public static HttpResponseMessage Down()
        {
            return new HttpResponseMessage(HttpStatusCode.BadGateway);
        }

        public List<Reported>? Players(int sync)
        {
            JsonElement players = Bodies[sync].GetProperty("players");

            if (players.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return players
                .EnumerateArray()
                .Select(player => new Reported(
                    player.GetProperty("steam_id").GetString()!,
                    player.GetProperty("name").GetString()!,
                    player.GetProperty("ip").GetString(),
                    player.GetProperty("kills").GetInt32(),
                    player.GetProperty("deaths").GetInt32()
                ))
                .OrderBy(player => player.SteamId)
                .ToList();
        }

        public List<string>? SteamIds(int sync)
        {
            return Players(sync)?.Select(player => player.SteamId).ToList();
        }

        public Dictionary<string, string> Conns(int sync)
        {
            return Bodies[sync]
                .GetProperty("players")
                .EnumerateArray()
                .ToDictionary(
                    player => player.GetProperty("steam_id").GetString()!,
                    player => player.GetProperty("conn").GetString()!
                );
        }

        public List<(string SteamId, int Kills, int Deaths)> Departed(int sync)
        {
            return Bodies[sync]
                .GetProperty("departed")
                .EnumerateArray()
                .Select(player =>
                    (
                        player.GetProperty("steam_id").GetString()!,
                        player.GetProperty("kills").GetInt32(),
                        player.GetProperty("deaths").GetInt32()
                    )
                )
                .ToList();
        }

        public string? Map(int sync)
        {
            return Bodies[sync].TryGetProperty("map", out JsonElement map)
                ? map.GetString()
                : null;
        }

        public List<string> DepartedConns(int sync)
        {
            return Bodies[sync]
                .GetProperty("departed")
                .EnumerateArray()
                .Select(player => player.GetProperty("conn").GetString()!)
                .ToList();
        }
    }

    private sealed class FailingRoster : PlayerRoster
    {
        public int ReportFailures;
        public bool ObserveFails;

        public override RosterReport Report(DateTimeOffset now)
        {
            if (ReportFailures > 0)
            {
                ReportFailures--;
                throw new InvalidOperationException("roster failed");
            }

            return base.Report(now);
        }

        public override DateTimeOffset? Observe(
            IEnumerable<ObservedPlayer> players,
            DateTimeOffset now
        )
        {
            if (ObserveFails)
            {
                throw new InvalidOperationException("roster failed");
            }

            return base.Observe(players, now);
        }
    }

    private static (
        SanctionSyncLoop Loop,
        PlayerRoster Roster,
        SanctionBook Book,
        Panel Panel
    ) Loop(PlayerRoster? roster = null)
    {
        Panel panel = new();
        SanctionBook book = new();
        roster ??= new PlayerRoster();

        SanctionSyncLoop loop = new(
            book,
            new ServerAccessBook(),
            roster,
            new SanctionsClient(new HttpClient(panel)),
            () =>
                new PlayerManagementSettings
                {
                    API_DOMAIN = "https://api.example.com",
                    SERVER_ID = "11111111-1111-1111-1111-111111111111",
                    SERVER_API_PASSWORD = "secret",
                },
            "0.0.9",
            "swiftlys2",
            () => default,
            () => false,
            _ => { },
            _ => { }
        );

        return (loop, roster, book, panel);
    }

    private static ObservedPlayer Human(
        string steamId,
        string? name = null,
        string? ip = "203.0.113.24:27005",
        bool verified = true
    )
    {
        return new ObservedPlayer(steamId, name ?? $"player {steamId}", ip, verified);
    }

    private static DateTimeOffset At(double seconds)
    {
        return Start.AddSeconds(seconds);
    }

    // The panel closes every session on [], so a plugin that has not looked yet
    // must not claim the server is empty.
    [Fact]
    public async Task TheRosterIsUnknownUntilTheFirstObservation()
    {
        var (loop, _, _, panel) = Loop();

        await loop.Tick(Start);

        Assert.Equal(JsonValueKind.Null, panel.Bodies[0].GetProperty("players").ValueKind);

        loop.Observe([Human("1")], At(1));
        await loop.Tick(At(1));
        Assert.Single(panel.Bodies);

        await loop.Tick(At(1) + PlayerRoster.Debounce);

        Assert.Equal(2, panel.Bodies.Count);
        Assert.Equal(["1"], panel.SteamIds(1));
    }

    [Fact]
    public async Task ABurstOfChangesIsOneSync()
    {
        var (loop, _, _, panel) = Loop();
        loop.Observe([Human("1")], Start);
        await loop.Tick(Start);

        loop.Observe([Human("1"), Human("2")], At(1));
        loop.Observe([Human("1"), Human("2"), Human("3")], At(2));
        await loop.Tick(At(2));
        Assert.Single(panel.Bodies);

        await loop.Tick(At(1) + PlayerRoster.Debounce);
        Assert.Equal(["1", "2", "3"], panel.SteamIds(1));

        loop.Observe([Human("1"), Human("2"), Human("3")], At(3));
        await loop.Tick(At(3) + PlayerRoster.Debounce);

        Assert.Equal(2, panel.Bodies.Count);
    }

    [Fact]
    public async Task ANameChangeSyncs()
    {
        var (loop, _, _, panel) = Loop();
        loop.Observe([Human("1", "nyx")], Start);
        await loop.Tick(Start);

        loop.Observe([Human("1", "nyx2")], At(1));
        await loop.Tick(At(1) + PlayerRoster.Debounce);

        Assert.Equal("nyx2", panel.Players(1)![0].Name);
    }

    // The join's own sync is what gets a banned player kicked; the roster
    // change rides along with it rather than waiting out the debounce.
    [Fact]
    public async Task AJoinIsNotHeldBackByTheDebounce()
    {
        var (loop, _, book, panel) = Loop();
        loop.Observe([Human("1")], Start);
        await loop.Tick(Start);

        book.Joined("2");
        loop.Request();
        loop.Observe([Human("1"), Human("2")], At(1));
        await loop.Tick(At(1));

        Assert.Equal(["1", "2"], panel.SteamIds(1));

        await loop.Tick(At(1) + PlayerRoster.Debounce);
        Assert.Equal(2, panel.Bodies.Count);
    }

    [Fact]
    public async Task OnlyVerifiedPlayersAreOnTheRoster()
    {
        var (loop, _, _, panel) = Loop();
        loop.Observe([Human("1"), Human("2", verified: false)], Start);

        await loop.Tick(Start);

        Assert.Equal(["1"], panel.SteamIds(0));
    }

    [Fact]
    public async Task ASilentServerIsEmptyOnceTheTicksStop()
    {
        var (loop, roster, _, panel) = Loop();
        loop.Observe([Human("1")], Start);
        roster.Died(null, "1", warmup: false);
        await loop.Tick(Start);

        await loop.Tick(Start + PlayerRoster.Silence);
        Assert.Single(panel.Bodies);

        DateTimeOffset silent = Start + PlayerRoster.Silence + TimeSpan.FromSeconds(1);
        await loop.Tick(silent);
        await loop.Tick(silent + PlayerRoster.Debounce);

        Assert.Equal(2, panel.Bodies.Count);
        Assert.Empty(panel.Players(1)!);
        Assert.Equal([("1", 1, 0)], panel.Departed(1));
    }

    [Fact]
    public async Task AWorldStillUpdatingWithoutTicksIsHibernating()
    {
        var (loop, roster, _, panel) = Loop();
        roster.WorldUpdated(Start);
        loop.Observe([Human("1")], Start);
        await loop.Tick(Start);

        for (int second = 1; second <= 5; second++)
        {
            roster.WorldUpdated(At(second));
        }

        await loop.Tick(At(5));
        Assert.Single(panel.Bodies);

        roster.WorldUpdated(At(6));
        await loop.Tick(At(6));
        await loop.Tick(At(6) + PlayerRoster.Debounce);

        Assert.Empty(panel.Players(1)!);
        Assert.Equal([("1", 0, 0)], panel.Departed(1));
    }

    [Fact]
    public void ARosterSaysItIsHibernatingOnceTheWorldOutlivesItsTicks()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1")], Start);

        for (int second = 1; second <= 5; second++)
        {
            roster.WorldUpdated(At(second));
        }

        Assert.False(roster.IsHibernating(At(5)));

        roster.WorldUpdated(At(6));

        Assert.True(roster.IsHibernating(At(6)));
    }

    [Fact]
    public void ATickingServerIsNotHibernating()
    {
        PlayerRoster roster = new();

        for (int second = 0; second <= 10; second++)
        {
            roster.Observe([], At(second));
            roster.WorldUpdated(At(second));
        }

        Assert.False(roster.IsHibernating(At(10)));
    }

    [Fact]
    public void AServerLoadingAMapIsNotHibernating()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1")], Start);
        roster.WorldUpdated(Start);

        Assert.False(roster.IsHibernating(At(30)));
    }

    // SwiftlyS2 hears of a map change only once the new map has loaded, and
    // until then nothing ticks or updates at all.
    [Fact]
    public async Task AWorldThatStopsWithItsTicksIsLoadingAMap()
    {
        var (loop, roster, _, panel) = Loop();
        roster.WorldUpdated(Start);
        loop.Observe([Human("1")], Start);
        await loop.Tick(Start);

        await loop.Tick(Start + SanctionSyncLoop.Interval);
        Assert.Equal(["1"], panel.SteamIds(1));

        DateTimeOffset gone = Start + PlayerRoster.HoldCap + TimeSpan.FromSeconds(1);
        await loop.Tick(gone);

        Assert.Empty(panel.Players(2)!);
        Assert.Equal([("1", 0, 0)], panel.Departed(2));
    }

    [Fact]
    public async Task TheHoldEndsAsSoonAsEveryoneIsBack()
    {
        var (loop, roster, _, panel) = Loop();
        loop.Observe([Human("1"), Human("2")], Start);
        await loop.Tick(Start);
        Dictionary<string, string> conns = panel.Conns(0);

        roster.MapEnded(At(1));
        await loop.Tick(At(1));
        await loop.Tick(At(1) + SanctionSyncLoop.Interval);

        Assert.Equal(["1", "2"], panel.SteamIds(1));
        Assert.Null(panel.Players(2));
        Assert.Empty(panel.Departed(2));

        roster.MapStarted(At(40), "de_inferno");
        loop.Observe([Human("1")], At(41));
        loop.Observe([Human("1"), Human("2")], At(50));
        await loop.Tick(At(50) + PlayerRoster.Debounce);

        Assert.Equal(["1", "2"], panel.SteamIds(3));
        Assert.Equal(conns, panel.Conns(3));
        Assert.Empty(panel.Departed(3));
    }

    // The first tick of a new map routinely finds nobody back yet. Settling on
    // it dropped everyone still loading and the live roster flickered on every
    // map change.
    [Fact]
    public async Task AnEmptyFirstObservationDropsNobodyStillLoading()
    {
        var (loop, roster, _, panel) = Loop();
        loop.Observe([Human("1"), Human("2")], Start);
        await loop.Tick(Start);
        Dictionary<string, string> conns = panel.Conns(0);

        roster.MapEnded(At(1));
        roster.MapStarted(At(2), "de_inferno");

        for (int second = 3; second <= 20; second++)
        {
            loop.Observe([], At(second));
            await loop.Tick(At(second));
        }

        loop.Observe([Human("1")], At(21));
        loop.Observe([Human("1"), Human("2")], At(25));
        await loop.Tick(At(25) + PlayerRoster.Debounce);

        // The map-end report, and nothing since.
        Assert.Equal(2, panel.Bodies.Count);

        loop.Observe([Human("1"), Human("2")], At(3) + SanctionSyncLoop.Interval);
        await loop.Tick(At(3) + SanctionSyncLoop.Interval);

        Assert.Equal(conns, panel.Conns(2));
        Assert.Empty(panel.Departed(2));
    }

    [Fact]
    public async Task WhoeverHasNotComeBackWhenTheHoldSettlesHasLeft()
    {
        var (loop, roster, _, panel) = Loop();
        loop.Observe([Human("1"), Human("2")], Start);
        await loop.Tick(Start);

        roster.MapEnded(At(1));
        await loop.Tick(At(2));
        roster.MapStarted(At(5), "de_inferno");
        loop.Observe([Human("1")], At(6));

        DateTimeOffset settled = At(6) + PlayerRoster.Settle;
        loop.Observe([Human("1")], settled - TimeSpan.FromSeconds(1));
        await loop.Tick(settled - TimeSpan.FromSeconds(1));
        Assert.Null(panel.Players(2));

        loop.Observe([Human("1")], settled);
        await loop.Tick(settled + PlayerRoster.Debounce);

        Assert.Equal(["1"], panel.SteamIds(3));
        Assert.Equal([("2", 0, 0)], panel.Departed(3));
    }

    [Fact]
    public async Task AHoldThatNeverSettlesEndsAfterTheCap()
    {
        var (loop, roster, _, panel) = Loop();
        loop.Observe([Human("1")], Start);
        await loop.Tick(Start);

        roster.MapEnded(At(1));
        DateTimeOffset capped = At(1) + PlayerRoster.HoldCap;

        for (DateTimeOffset now = At(2); now < capped; now += TimeSpan.FromSeconds(1))
        {
            loop.Observe([Human("1"), Human("2")], now);
        }

        loop.Observe([Human("1"), Human("2")], capped);
        await loop.Tick(capped + PlayerRoster.Debounce);

        Assert.Equal(["1", "2"], panel.SteamIds(panel.Bodies.Count - 1));
    }

    // A server can hibernate straight through a map change, and the ticks that
    // would settle the hold never come.
    [Fact]
    public async Task AHoldTheTicksNeverReturnToEndsEmpty()
    {
        var (loop, roster, _, panel) = Loop();
        loop.Observe([Human("1")], Start);
        await loop.Tick(Start);

        roster.MapEnded(At(1));
        roster.MapStarted(At(2), "de_inferno");

        DateTimeOffset capped = At(1) + PlayerRoster.HoldCap;
        await loop.Tick(capped);
        await loop.Tick(capped + PlayerRoster.Debounce);

        Assert.Empty(panel.Players(panel.Bodies.Count - 1)!);
        Assert.Equal([("1", 0, 0)], panel.Departed(panel.Bodies.Count - 1));
    }

    [Fact]
    public async Task HibernatingEmptiesTheRosterWithoutWaitingForSilence()
    {
        var (loop, roster, _, panel) = Loop();
        loop.Observe([Human("1")], Start);
        await loop.Tick(Start);

        roster.Hibernating(At(1));
        await loop.Tick(At(1) + PlayerRoster.Debounce);

        Assert.Empty(panel.Players(1)!);
        Assert.Equal([("1", 0, 0)], panel.Departed(1));
    }

    [Fact]
    public async Task CountersBelongToTheConnectionAndStartOverOnRejoin()
    {
        var (loop, roster, _, panel) = Loop();
        loop.Observe([Human("1"), Human("2")], Start);
        roster.Died("2", "1", warmup: false);
        roster.Died("2", "1", warmup: false);
        roster.Died("1", null, warmup: false);
        roster.Died(null, "bot", warmup: false);
        await loop.Tick(Start);

        Assert.Equal(
            [
                new Reported("1", "player 1", "203.0.113.24", 2, 1),
                new Reported("2", "player 2", "203.0.113.24", 0, 2),
            ],
            panel.Players(0)
        );

        loop.Observe([Human("2")], At(1));
        loop.Observe([Human("1"), Human("2")], At(2));
        roster.Died("2", "1", warmup: false);
        await loop.Tick(At(1) + PlayerRoster.Debounce);

        Assert.Equal(1, panel.Players(1)![0].Kills);
        Assert.Equal([("1", 2, 1)], panel.Departed(1));
        Assert.Equal([panel.Conns(0)["1"]], panel.DepartedConns(1));
        Assert.NotEqual(panel.Conns(0)["1"], panel.Conns(1)["1"]);
        Assert.Equal(panel.Conns(0)["2"], panel.Conns(1)["2"]);
    }

    [Fact]
    public void EveryConnectionGetsItsOwnId()
    {
        PlayerRoster first = new();
        PlayerRoster reloaded = new();
        first.Observe([Human("1"), Human("2")], Start);
        reloaded.Observe([Human("1")], Start);

        List<string> conns = first
            .Report(Start)
            .Players!.Concat(reloaded.Report(Start).Players!)
            .Select(player => player.conn)
            .ToList();

        Assert.Equal(3, conns.Distinct().Count());
        Assert.All(conns, conn => Assert.Matches("^[0-9a-f]{32}$", conn));
    }

    [Fact]
    public async Task DeparturesAreKeptUntilASyncSucceeds()
    {
        var (loop, roster, _, panel) = Loop();
        loop.Observe([Human("1")], Start);
        await loop.Tick(Start);

        roster.Died(null, "1", warmup: false);
        loop.Observe([], At(1));
        panel.Answer = () => Task.FromResult(Panel.Down());

        await loop.Tick(At(1) + PlayerRoster.Debounce);
        await loop.Tick(At(1) + PlayerRoster.Debounce + SanctionSyncLoop.Interval);

        Assert.Equal([("1", 1, 0)], panel.Departed(1));
        Assert.Equal([("1", 1, 0)], panel.Departed(2));

        panel.Answer = () => Task.FromResult(Panel.Ok());
        await loop.Tick(At(1) + PlayerRoster.Debounce + SanctionSyncLoop.Interval * 2);
        await loop.Tick(At(1) + PlayerRoster.Debounce + SanctionSyncLoop.Interval * 3);

        Assert.Equal([("1", 1, 0)], panel.Departed(3));
        Assert.Empty(panel.Departed(4));
    }

    [Fact]
    public async Task DeparturesThePanelDidNotRecordAreSentAgain()
    {
        var (loop, _, _, panel) = Loop();
        loop.Observe([Human("1")], Start);
        await loop.Tick(Start);

        loop.Observe([], At(1));
        panel.Answer = () => Task.FromResult(Panel.Ok(recorded: false));
        await loop.Tick(At(1) + PlayerRoster.Debounce);

        panel.Answer = () => Task.FromResult(Panel.Ok(recorded: true));
        await loop.Tick(At(1) + PlayerRoster.Debounce + SanctionSyncLoop.Interval);
        await loop.Tick(At(1) + PlayerRoster.Debounce + SanctionSyncLoop.Interval * 2);

        Assert.Equal([("1", 0, 0)], panel.Departed(1));
        Assert.Equal([("1", 0, 0)], panel.Departed(2));
        Assert.Empty(panel.Departed(3));
    }

    // A map change leaves no sync without a roster that has departures to
    // carry, so this one is made by hand.
    [Fact]
    public void ASyncWithoutARosterDoesNotSettleDepartures()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);
        roster.Observe([Human("1")], At(1));

        RosterReport report = roster.Report(At(2));
        roster.Delivered(report with { Players = null }, true);

        Assert.Equal(["2"], roster.Report(At(3)).Departed.Select(player => player.steam_id));
    }

    [Fact]
    public async Task TheMapEndReportCarriesAndSettlesTheDeparturesBeforeIt()
    {
        var (loop, roster, _, panel) = Loop();
        loop.Observe([Human("1"), Human("2")], Start);
        await loop.Tick(Start);

        loop.Observe([Human("1")], At(1));
        roster.MapEnded(At(2));
        await loop.Tick(At(2));

        Assert.Equal(["1"], panel.SteamIds(1));
        Assert.Equal([("2", 0, 0)], panel.Departed(1));

        roster.MapStarted(At(3), "de_inferno");
        loop.Observe([Human("1")], At(4));
        loop.Observe([Human("1")], At(2) + SanctionSyncLoop.Interval);
        await loop.Tick(At(2) + SanctionSyncLoop.Interval);

        DateTimeOffset next = At(2) + SanctionSyncLoop.Interval * 2;
        loop.Observe([Human("1")], next);
        await loop.Tick(next);

        // Everyone held is back, so the new map's roster is known at once.
        Assert.Equal(["1"], panel.SteamIds(2));
        Assert.Equal(["1"], panel.SteamIds(3));
        Assert.Empty(panel.Departed(2));
        Assert.Empty(panel.Departed(3));
    }

    // The panel splits a session's counts by the map each report names, so the
    // kills between the last sync and the end of a map have to be reported as
    // that map's before the next one starts.
    [Fact]
    public async Task AMapsLastKillsAreReportedOnThatMap()
    {
        var (loop, roster, _, panel) = Loop();
        roster.MapIs("de_mirage");
        loop.Observe([Human("1"), Human("2")], Start);
        await loop.Tick(Start);

        roster.Died("2", "1", warmup: false);
        roster.Died("2", "1", warmup: false);
        roster.MapEnded(At(5));
        roster.MapStarted(At(6), "de_inferno");
        await loop.Tick(At(6));

        Assert.Equal("de_mirage", panel.Map(0));
        Assert.Equal("de_mirage", panel.Map(1));
        Assert.Equal(
            [
                new Reported("1", "player 1", "203.0.113.24", 2, 0),
                new Reported("2", "player 2", "203.0.113.24", 0, 2),
            ],
            panel.Players(1)
        );

        await loop.Tick(At(6) + SanctionSyncLoop.Interval);
        Assert.Null(panel.Players(2));
        Assert.Equal("de_inferno", panel.Map(2));

        loop.Observe([Human("1"), Human("2")], At(6) + SanctionSyncLoop.Interval * 2);
        roster.Died("1", "2", warmup: false);
        await loop.Tick(At(6) + SanctionSyncLoop.Interval * 2);

        Assert.Equal("de_inferno", panel.Map(3));
        Assert.Equal(
            [
                new Reported("1", "player 1", "203.0.113.24", 2, 1),
                new Reported("2", "player 2", "203.0.113.24", 1, 2),
            ],
            panel.Players(3)
        );
    }

    [Fact]
    public async Task AMapEndReportThePanelMissedIsSentAgain()
    {
        var (loop, roster, _, panel) = Loop();
        roster.MapIs("de_mirage");
        loop.Observe([Human("1")], Start);
        await loop.Tick(Start);

        roster.Died(null, "1", warmup: false);
        roster.MapEnded(At(5));
        roster.MapStarted(At(6), "de_inferno");
        panel.Answer = () => Task.FromResult(Panel.Down());
        await loop.Tick(At(6));

        panel.Answer = () => Task.FromResult(Panel.Ok(recorded: true));
        await loop.Tick(At(6) + SanctionSyncLoop.Interval);
        await loop.Tick(At(6) + SanctionSyncLoop.Interval * 2);

        foreach (int sync in new[] { 1, 2 })
        {
            Assert.Equal("de_mirage", panel.Map(sync));
            Assert.Equal(
                [new Reported("1", "player 1", "203.0.113.24", 1, 0)],
                panel.Players(sync)
            );
        }

        Assert.Null(panel.Players(3));
        Assert.Equal("de_inferno", panel.Map(3));
    }

    // The sync in flight carries the roster from before the last kills, so the
    // panel taking it must not stand in for the map-end report.
    [Fact]
    public async Task ASyncInFlightAtTheMapChangeIsNotTheMapEndReport()
    {
        var (loop, roster, _, panel) = Loop();
        roster.MapIs("de_mirage");
        loop.Observe([Human("1"), Human("2")], Start);
        await loop.Tick(Start);

        roster.Died("2", "1", warmup: false);

        TaskCompletionSource<HttpResponseMessage> answer = new();
        panel.Answer = () => answer.Task;
        loop.Observe([Human("1"), Human("2")], Start + SanctionSyncLoop.Interval);
        Task inFlight = loop.Tick(Start + SanctionSyncLoop.Interval);

        roster.Died("2", "1", warmup: false);
        roster.MapEnded(At(31));
        roster.MapStarted(At(32), "de_inferno");
        answer.SetResult(Panel.Ok(recorded: true));
        await inFlight;

        panel.Answer = () => Task.FromResult(Panel.Ok(recorded: true));
        await loop.Tick(At(32));

        Assert.Equal("de_mirage", panel.Map(1));
        Assert.Equal(
            [
                new Reported("1", "player 1", "203.0.113.24", 1, 0),
                new Reported("2", "player 2", "203.0.113.24", 0, 1),
            ],
            panel.Players(1)
        );

        Assert.Equal("de_mirage", panel.Map(2));
        Assert.Equal(
            [
                new Reported("1", "player 1", "203.0.113.24", 2, 0),
                new Reported("2", "player 2", "203.0.113.24", 0, 2),
            ],
            panel.Players(2)
        );

        await loop.Tick(At(32) + SanctionSyncLoop.Interval);

        Assert.Equal(4, panel.Bodies.Count);
        Assert.Equal("de_inferno", panel.Map(3));
        Assert.Null(panel.Players(3));
    }

    // A kill on the new map while its players are still loading in belongs to
    // that map, even when the old map's report is sent after it.
    [Fact]
    public async Task AKillDuringTheHoldIsNotInTheMapEndReport()
    {
        var (loop, roster, _, panel) = Loop();
        roster.MapIs("de_mirage");
        loop.Observe([Human("1"), Human("2")], Start);
        await loop.Tick(Start);

        roster.Died("2", "1", warmup: false);
        roster.MapEnded(At(5));
        roster.MapStarted(At(6), "de_inferno");
        roster.Died("2", "1", warmup: false);

        panel.Answer = () => Task.FromResult(Panel.Down());
        await loop.Tick(At(6));

        roster.Died("2", "1", warmup: false);

        panel.Answer = () => Task.FromResult(Panel.Ok(recorded: true));
        await loop.Tick(At(6) + SanctionSyncLoop.Interval);

        foreach (int sync in new[] { 1, 2 })
        {
            Assert.Equal("de_mirage", panel.Map(sync));
            Assert.Equal(
                [
                    new Reported("1", "player 1", "203.0.113.24", 1, 0),
                    new Reported("2", "player 2", "203.0.113.24", 0, 1),
                ],
                panel.Players(sync)
            );
        }

        loop.Observe([Human("1"), Human("2")], At(6) + SanctionSyncLoop.Interval * 2);
        await loop.Tick(At(6) + SanctionSyncLoop.Interval * 2);

        Assert.Equal(4, panel.Bodies.Count);
        Assert.Equal("de_inferno", panel.Map(3));
        Assert.Equal(
            [
                new Reported("1", "player 1", "203.0.113.24", 3, 0),
                new Reported("2", "player 2", "203.0.113.24", 0, 3),
            ],
            panel.Players(3)
        );
    }

    // A roster that changed over the map change differs from the report still
    // waiting to be taken; that must not turn every poll into a sync.
    [Fact]
    public async Task ARefusedMapEndReportIsOnlyRetriedOnTheInterval()
    {
        var (loop, roster, _, panel) = Loop();
        roster.MapIs("de_mirage");
        loop.Observe([Human("1"), Human("2")], Start);
        await loop.Tick(Start);

        roster.MapEnded(At(5));
        roster.MapStarted(At(6), "de_inferno");
        panel.Answer = () => Task.FromResult(Panel.Ok(recorded: false));

        DateTimeOffset now = At(6);

        for (; now < At(80); now += TimeSpan.FromSeconds(0.5))
        {
            loop.Observe([Human("1")], now);
            await loop.Tick(now);
        }

        // At 6, 36 and 66 seconds, though the hold settled at 52.
        Assert.Equal(4, panel.Bodies.Count);

        for (int sync = 1; sync < 4; sync++)
        {
            Assert.Equal("de_mirage", panel.Map(sync));
            Assert.Equal(["1", "2"], panel.SteamIds(sync));
        }

        panel.Answer = () => Task.FromResult(Panel.Ok(recorded: true));

        for (; now < At(100); now += TimeSpan.FromSeconds(0.5))
        {
            loop.Observe([Human("1")], now);
            await loop.Tick(now);
        }

        Assert.Equal(6, panel.Bodies.Count);
        Assert.Equal("de_mirage", panel.Map(4));
        Assert.Equal("de_inferno", panel.Map(5));
        Assert.Equal(["1"], panel.SteamIds(5));
        Assert.Equal([("2", 0, 0)], panel.Departed(5));
    }

    [Fact]
    public async Task AMapEndReportIsGivenUpAfterTheCap()
    {
        var (loop, roster, _, panel) = Loop();
        roster.MapIs("de_mirage");
        loop.Observe([Human("1")], Start);
        await loop.Tick(Start);

        roster.MapEnded(At(5));
        roster.MapStarted(At(6), "de_inferno");
        panel.Answer = () => Task.FromResult(Panel.Down());

        DateTimeOffset now = At(6);

        for (; now < At(5) + PlayerRoster.HoldCap; now += SanctionSyncLoop.Interval)
        {
            await loop.Tick(now);
            Assert.Equal("de_mirage", panel.Map(panel.Bodies.Count - 1));
        }

        panel.Answer = () => Task.FromResult(Panel.Ok(recorded: true));
        loop.Observe([Human("1")], now);
        await loop.Tick(now);

        Assert.Equal("de_inferno", panel.Map(panel.Bodies.Count - 1));
        Assert.Equal(["1"], panel.SteamIds(panel.Bodies.Count - 1));
    }

    [Fact]
    public async Task AnUnknownRosterHasNoMapEndReport()
    {
        var (loop, roster, _, panel) = Loop();
        roster.MapEnded(At(1));
        roster.MapStarted(At(2), "de_inferno");
        await loop.Tick(At(2));

        Assert.Single(panel.Bodies);
        Assert.Null(panel.Players(0));
        Assert.Equal("de_inferno", panel.Map(0));
    }

    [Fact]
    public async Task ABlankMapIsNoMap()
    {
        var (loop, roster, _, panel) = Loop();
        roster.MapIs("  ");
        loop.Observe([Human("1")], Start);
        await loop.Tick(Start);

        Assert.Equal(JsonValueKind.Null, panel.Bodies[0].GetProperty("map").ValueKind);
    }

    [Fact]
    public async Task ADepartureWhileASyncIsInFlightWaitsForTheNext()
    {
        var (loop, _, _, panel) = Loop();
        loop.Observe([Human("1"), Human("2")], Start);
        await loop.Tick(Start);

        TaskCompletionSource<HttpResponseMessage> answer = new();
        panel.Answer = () => answer.Task;

        loop.Observe([Human("2")], At(1));
        Task inFlight = loop.Tick(At(1) + PlayerRoster.Debounce);

        loop.Observe([], At(3));
        answer.SetResult(Panel.Ok());
        await inFlight;

        panel.Answer = () => Task.FromResult(Panel.Ok());
        await loop.Tick(At(3) + PlayerRoster.Debounce);

        Assert.Equal([("1", 0, 0)], panel.Departed(1));
        Assert.Equal([("2", 0, 0)], panel.Departed(2));
    }

    // Each connection settles against its own session, so two of them are
    // never folded into one.
    [Fact]
    public void EachConnectionDepartsOnItsOwn()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1")], Start);
        roster.Died(null, "1", warmup: false);
        roster.Observe([], At(1));
        roster.Observe([Human("1")], At(2));
        roster.Died(null, "1", warmup: false);
        roster.Died(null, "1", warmup: false);
        roster.Died("1", null, warmup: false);
        roster.Observe([], At(3));

        RosterReport report = roster.Report(At(4));

        Assert.Equal(
            [("1", 1, 0), ("1", 2, 1)],
            report.Departed.Select(departed => (departed.steam_id, departed.kills, departed.deaths))
        );
        Assert.NotEqual(report.Departed[0].conn, report.Departed[1].conn);
    }

    private sealed record Counters(
        int Kills,
        int Deaths,
        int Assists,
        int Headshots,
        int Damage,
        int RoundsPlayed,
        int RoundsWon,
        int Mvps
    );

    private static Counters CountersOf(RosterPlayer player)
    {
        return new Counters(
            player.kills,
            player.deaths,
            player.assists,
            player.headshots,
            player.damage,
            player.rounds_played,
            player.rounds_won,
            player.mvps
        );
    }

    private static Counters CountersOf(DepartedPlayer player)
    {
        return new Counters(
            player.kills,
            player.deaths,
            player.assists,
            player.headshots,
            player.damage,
            player.rounds_played,
            player.rounds_won,
            player.mvps
        );
    }

    private static Counters CountersOf(RosterReport report, string steamId)
    {
        return CountersOf(report.Players!.Single(player => player.steam_id == steamId));
    }

    private static (int Played, int Won) Rounds(RosterReport report, string steamId)
    {
        Counters counters = CountersOf(report, steamId);

        return (counters.RoundsPlayed, counters.RoundsWon);
    }

    private static Dictionary<string, int> Teams(params (string SteamId, int Team)[] teams)
    {
        return teams.ToDictionary(team => team.SteamId, team => team.Team);
    }

    [Fact]
    public void AssistsAndHeadshotsGoToRosterPlayersOnly()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2"), Human("3")], Start);

        roster.Died("2", "1", warmup: false, assister: "3", headshot: true);
        roster.Died("2", "1", warmup: false, assister: "bot", headshot: false);
        roster.Died("3", "bot", warmup: false, assister: "1", headshot: true);
        roster.Died("3", null, warmup: false, assister: null, headshot: true);

        RosterReport report = roster.Report(At(1));

        Assert.Equal(new Counters(2, 0, 1, 1, 0, 0, 0, 0), CountersOf(report, "1"));
        Assert.Equal(new Counters(0, 2, 0, 0, 0, 0, 0, 0), CountersOf(report, "2"));
        Assert.Equal(new Counters(0, 2, 1, 0, 0, 0, 0, 0), CountersOf(report, "3"));
    }

    // The hooks pass no killer for a kill that does not count, so a team kill's
    // headshot is not one.
    [Fact]
    public void AHeadshotCountsOnlyWithACountedKill()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);
        bool teamKill = PlayerRoster.CountsAsKill(
            1,
            2,
            PlayerRoster.TeamT,
            PlayerRoster.TeamT,
            false
        );

        roster.Died("2", teamKill ? "1" : null, warmup: false, headshot: true);
        roster.Died("2", "1", warmup: false, assister: null, headshot: true);

        Assert.Equal(1, CountersOf(roster.Report(At(1)), "1").Headshots);
    }

    [Theory]
    [InlineData(1, 2, 2, 3, false, false, true)]
    [InlineData(1, 2, 2, 3, false, true, false)]
    [InlineData(1, 2, 2, 2, false, false, false)]
    [InlineData(1, 2, 2, 2, true, false, true)]
    [InlineData(1, 1, 2, 2, true, false, false)]
    public void AnAssistIsDamageToAnEnemyNotAFlash(
        int assister,
        int victim,
        int assisterTeam,
        int victimTeam,
        bool teammatesAreEnemies,
        bool flash,
        bool expected
    )
    {
        Assert.Equal(
            expected,
            PlayerRoster.CountsAsAssist(
                assister,
                victim,
                assisterTeam,
                victimTeam,
                teammatesAreEnemies,
                flash
            )
        );
    }

    [Fact]
    public void AFlashAssistEarnsNothing()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2"), Human("3")], Start);
        bool flash = PlayerRoster.CountsAsAssist(
            3,
            2,
            PlayerRoster.TeamCT,
            PlayerRoster.TeamT,
            false,
            true
        );
        bool damage = PlayerRoster.CountsAsAssist(
            3,
            2,
            PlayerRoster.TeamCT,
            PlayerRoster.TeamT,
            false,
            false
        );

        roster.Died("2", "1", warmup: false, assister: flash ? "3" : null);
        roster.Died("2", "1", warmup: false, assister: damage ? "3" : null);

        Assert.Equal(1, CountersOf(roster.Report(At(1)), "3").Assists);
    }

    [Fact]
    public void DamageIsCountedForTheAttackerOnly()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);

        roster.Hurt("1", "2", 27, warmup: false);
        roster.Hurt("1", "2", 73, warmup: false);
        roster.Hurt("1", "1", 50, warmup: false);
        roster.Hurt("1", "2", 0, warmup: false);
        roster.Hurt("1", "2", -5, warmup: false);
        roster.Hurt("bot", "2", 40, warmup: false);
        roster.Hurt(null, "2", 40, warmup: false);
        roster.Hurt("1", null, 10, warmup: false);

        RosterReport report = roster.Report(At(1));

        Assert.Equal(110, CountersOf(report, "1").Damage);
        Assert.Equal(0, CountersOf(report, "2").Damage);
    }

    // Damage goes by the kill rule: a teammate's or one's own health is no
    // damage, unless teammates are enemies.
    [Theory]
    [InlineData(1, 2, 2, 3, false, 30)]
    [InlineData(1, 2, 2, 2, false, 0)]
    [InlineData(1, 1, 2, 2, false, 0)]
    [InlineData(1, 2, 2, 2, true, 30)]
    public void TeamDamageEarnsNothing(
        int attacker,
        int victim,
        int attackerTeam,
        int victimTeam,
        bool teammatesAreEnemies,
        int expected
    )
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);
        bool enemy = PlayerRoster.CountsAsKill(
            attacker,
            victim,
            attackerTeam,
            victimTeam,
            teammatesAreEnemies
        );

        roster.Hurt(enemy ? "1" : null, attacker == victim ? "1" : "2", 30, warmup: false);

        Assert.Equal(expected, CountersOf(roster.Report(At(1)), "1").Damage);
    }

    [Theory]
    [InlineData(27, 73, null, 27)]
    [InlineData(27, 73, 40, 27)]
    [InlineData(448, 0, null, 100)]
    [InlineData(448, 0, 31, 31)]
    [InlineData(20, 0, 31, 20)]
    [InlineData(448, 0, 0, 0)]
    [InlineData(-3, 50, null, 0)]
    public void ALethalHitIsCappedAtTheHealthThatWasLeft(
        int damage,
        int healthAfter,
        int? healthBefore,
        int expected
    )
    {
        Assert.Equal(expected, PlayerRoster.Dealt(damage, healthAfter, healthBefore));
    }

    // Player 1 switches sides between the rounds and wins both: a round goes to
    // the side a player ends it on.
    [Fact]
    public void ARoundIsPlayedByEveryoneOnASideAndWonByTheWinners()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2"), Human("3"), Human("4")], Start);
        roster.FreezeTimeEnded(warmup: false);

        roster.RoundEnded(
            PlayerRoster.TeamT,
            Teams(
                ("1", PlayerRoster.TeamT),
                ("2", PlayerRoster.TeamCT),
                ("3", 1),
                ("4", 0),
                ("bot", PlayerRoster.TeamT)
            ),
            warmup: false
        );
        roster.FreezeTimeEnded(warmup: false);
        roster.RoundEnded(
            PlayerRoster.TeamCT,
            Teams(
                ("1", PlayerRoster.TeamCT),
                ("2", PlayerRoster.TeamCT),
                ("3", PlayerRoster.TeamT)
            ),
            warmup: false
        );

        RosterReport report = roster.Report(At(1));

        Assert.Equal((2, 2), Rounds(report, "1"));
        Assert.Equal((2, 1), Rounds(report, "2"));
        Assert.Equal((1, 0), Rounds(report, "3"));
        Assert.Equal((0, 0), Rounds(report, "4"));
    }

    // A draw has no winners, and is still a round everybody on a side played.
    [Fact]
    public void ADrawIsPlayedAndWonByNobody()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);
        roster.FreezeTimeEnded(warmup: false);

        roster.RoundEnded(
            1,
            Teams(("1", PlayerRoster.TeamT), ("2", PlayerRoster.TeamCT)),
            warmup: false
        );

        RosterReport report = roster.Report(At(1));

        Assert.Equal((1, 0), Rounds(report, "1"));
        Assert.Equal((1, 0), Rounds(report, "2"));
    }

    [Fact]
    public void WarmupRoundsAreNotCounted()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1")], Start);
        roster.FreezeTimeEnded(warmup: true);

        roster.RoundEnded(PlayerRoster.TeamT, Teams(("1", PlayerRoster.TeamT)), warmup: true);

        Assert.Equal(new Counters(0, 0, 0, 0, 0, 0, 0, 0), CountersOf(roster.Report(At(1)), "1"));
    }

    // Warmup ends without a round of its own: what players do in it is
    // dropped, and what they do after it counts.
    [Fact]
    public void NothingDoneInWarmupIsCounted()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2"), Human("3")], Start);
        roster.FreezeTimeEnded(warmup: true);

        roster.Died("2", "1", warmup: true, assister: "3", headshot: true);
        roster.Hurt("1", "2", 100, warmup: true);
        roster.Mvp("1", warmup: true);
        roster.RoundEnded(PlayerRoster.TeamT, Teams(("1", PlayerRoster.TeamT)), warmup: true);

        roster.FreezeTimeEnded(warmup: false);
        roster.Died("2", "1", warmup: false, assister: "3", headshot: true);
        roster.Hurt("1", "2", 100, warmup: false);
        roster.Mvp("1", warmup: false);

        RosterReport report = roster.Report(At(1));

        Assert.Equal(new Counters(1, 0, 0, 1, 100, 0, 0, 1), CountersOf(report, "1"));
        Assert.Equal(new Counters(0, 1, 0, 0, 0, 0, 0, 0), CountersOf(report, "2"));
        Assert.Equal(new Counters(0, 0, 1, 0, 0, 0, 0, 0), CountersOf(report, "3"));
    }

    // Ending warmup ends its round as a win for whoever is alive, and the warmup
    // flag can already be off by then. Only the round after it counts.
    [Fact]
    public void EndingWarmupHandsOutNoRound()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);
        roster.FreezeTimeEnded(warmup: true);

        roster.RoundEnded(
            PlayerRoster.TeamT,
            Teams(("1", PlayerRoster.TeamT), ("2", PlayerRoster.TeamCT)),
            warmup: false
        );
        roster.Mvp("1", warmup: false);

        Assert.Equal(new Counters(0, 0, 0, 0, 0, 0, 0, 0), CountersOf(roster.Report(At(1)), "1"));

        roster.FreezeTimeEnded(warmup: false);
        roster.RoundEnded(
            PlayerRoster.TeamT,
            Teams(("1", PlayerRoster.TeamT), ("2", PlayerRoster.TeamCT)),
            warmup: false
        );
        roster.Mvp("1", warmup: false);

        RosterReport report = roster.Report(At(2));

        Assert.Equal((1, 1), Rounds(report, "1"));
        Assert.Equal(1, CountersOf(report, "1").Mvps);
        Assert.Equal((1, 0), Rounds(report, "2"));
    }

    // An admin or a plugin can end a round again before the next one starts,
    // and the next round only counts once its own freeze time has ended.
    [Fact]
    public void ARoundCountsOnceAndTheNextOneOnlyOnceItGoesLive()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);
        IReadOnlyDictionary<string, int> teams = Teams(
            ("1", PlayerRoster.TeamT),
            ("2", PlayerRoster.TeamCT)
        );

        roster.FreezeTimeEnded(warmup: false);
        roster.RoundEnded(PlayerRoster.TeamT, teams, warmup: false);
        roster.Mvp("1", warmup: false);
        roster.RoundEnded(PlayerRoster.TeamCT, teams, warmup: false);

        Assert.Equal((1, 1), Rounds(roster.Report(At(1)), "1"));
        Assert.Equal((1, 0), Rounds(roster.Report(At(1)), "2"));

        roster.RoundStarted();
        roster.RoundEnded(PlayerRoster.TeamCT, teams, warmup: false);
        roster.Mvp("2", warmup: false);

        RosterReport report = roster.Report(At(2));
        Assert.Equal((1, 0), Rounds(report, "2"));
        Assert.Equal(0, CountersOf(report, "2").Mvps);
        Assert.Equal(1, CountersOf(report, "1").Mvps);
    }

    // A changelevel cuts the round under way short, and the new map's first
    // round has not gone live until its freeze time ends.
    [Fact]
    public void ARoundTheMapChangeCutShortIsNotCounted()
    {
        PlayerRoster roster = new();
        roster.MapIs("de_mirage");
        roster.Observe([Human("1")], Start);
        roster.FreezeTimeEnded(warmup: false);

        roster.MapEnded(At(1));
        roster.Delivered(roster.Report(At(1)), true);
        roster.MapStarted(At(2), "de_inferno");
        roster.Observe([Human("1")], At(3));
        roster.RoundEnded(PlayerRoster.TeamT, Teams(("1", PlayerRoster.TeamT)), warmup: false);
        roster.Mvp("1", warmup: false);

        // The old map's report is delivered and everyone held is back, so this
        // is the new map's roster and not the snapshot taken at the map's end.
        RosterReport report = roster.Report(At(4));
        Assert.NotNull(report.Players);
        Assert.Equal("de_inferno", report.Map);
        Assert.Equal(new Counters(0, 0, 0, 0, 0, 0, 0, 0), CountersOf(report, "1"));
    }

    // The MVP comes after the round end, and a round ended again has had its
    // MVP, whether the second end comes before or after the first MVP and
    // whether that MVP went to a bot.
    [Fact]
    public void ARoundEndedAgainGetsNoSecondMvp()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);
        IReadOnlyDictionary<string, int> teams = Teams(
            ("1", PlayerRoster.TeamT),
            ("2", PlayerRoster.TeamCT)
        );

        roster.FreezeTimeEnded(warmup: false);
        roster.RoundEnded(PlayerRoster.TeamT, teams, warmup: false);
        roster.Mvp("1", warmup: false);
        roster.RoundEnded(PlayerRoster.TeamCT, teams, warmup: false);
        roster.Mvp("2", warmup: false);

        roster.RoundStarted();
        roster.FreezeTimeEnded(warmup: false);
        roster.RoundEnded(PlayerRoster.TeamCT, teams, warmup: false);
        roster.RoundEnded(PlayerRoster.TeamT, teams, warmup: false);
        roster.Mvp("2", warmup: false);
        roster.Mvp("1", warmup: false);

        roster.RoundStarted();
        roster.FreezeTimeEnded(warmup: false);
        roster.RoundEnded(PlayerRoster.TeamT, teams, warmup: false);
        roster.Mvp(null, warmup: false);
        roster.RoundEnded(PlayerRoster.TeamCT, teams, warmup: false);
        roster.Mvp("2", warmup: false);

        RosterReport report = roster.Report(At(1));
        Assert.Equal(1, CountersOf(report, "1").Mvps);
        Assert.Equal(1, CountersOf(report, "2").Mvps);
    }

    [Fact]
    public void AnMvpGoesToARosterPlayerOnly()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1")], Start);
        roster.FreezeTimeEnded(warmup: false);

        roster.Mvp("1", warmup: false);
        roster.Mvp("1", warmup: false);
        roster.Mvp("bot", warmup: false);
        roster.Mvp(null, warmup: false);

        Assert.Equal(2, CountersOf(roster.Report(At(1)), "1").Mvps);
    }

    private static void CountEverything(PlayerRoster roster, string steamId, string enemy)
    {
        roster.FreezeTimeEnded(warmup: false);
        roster.Died(enemy, steamId, warmup: false, assister: null, headshot: true);
        roster.Died(steamId, enemy, warmup: false);
        roster.Died(enemy, null, warmup: false, assister: steamId);
        roster.Hurt(steamId, enemy, 64, warmup: false);
        roster.RoundEnded(
            PlayerRoster.TeamT,
            Teams((steamId, PlayerRoster.TeamT), (enemy, PlayerRoster.TeamCT)),
            warmup: false
        );
        roster.Mvp(steamId, warmup: false);
    }

    [Fact]
    public async Task EveryCounterStartsOverWithANewConnectionAndDepartsWithItsFinalValue()
    {
        var (loop, roster, _, panel) = Loop();
        loop.Observe([Human("1"), Human("2")], Start);
        CountEverything(roster, "1", "2");
        CountEverything(roster, "1", "2");
        await loop.Tick(Start);

        loop.Observe([Human("2")], At(1));
        loop.Observe([Human("1"), Human("2")], At(2));
        CountEverything(roster, "1", "2");
        await loop.Tick(At(1) + PlayerRoster.Debounce);

        JsonElement departed = Assert.Single(
            panel.Bodies[1].GetProperty("departed").EnumerateArray()
        );
        JsonElement current = panel
            .Bodies[1]
            .GetProperty("players")
            .EnumerateArray()
            .Single(player => player.GetProperty("steam_id").GetString() == "1");

        Assert.Equal(panel.Conns(0)["1"], departed.GetProperty("conn").GetString());
        Assert.Equal(
            new Counters(2, 2, 2, 2, 128, 2, 2, 2),
            JsonSerializer.Deserialize<Counters>(departed.GetRawText(), Snake)
        );
        Assert.Equal(
            new Counters(1, 1, 1, 1, 64, 1, 1, 1),
            JsonSerializer.Deserialize<Counters>(current.GetRawText(), Snake)
        );
    }

    private static readonly JsonSerializerOptions Snake = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    // A departure is a snapshot: counting more for a player who has left
    // changes nothing about the connection that ended.
    [Fact]
    public void ADepartureKeepsTheCountersItLeftWith()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);
        CountEverything(roster, "1", "2");
        roster.Observe([Human("2")], At(1));
        CountEverything(roster, "1", "2");

        DepartedPlayer departed = Assert.Single(roster.Report(At(2)).Departed);

        Assert.Equal(new Counters(1, 1, 1, 1, 64, 1, 1, 1), CountersOf(departed));
    }

    [Fact]
    public void DeparturesGoOutOldestFirstAndAtMostSixtyFourAtATime()
    {
        PlayerRoster roster = new();
        List<ObservedPlayer> present = Enumerable
            .Range(1, 70)
            .Select(id => Human(id.ToString()))
            .ToList();
        roster.Observe(present, Start);

        for (int left = 1; left <= 70; left++)
        {
            roster.Observe(present.Skip(left), At(left));
        }

        RosterReport first = roster.Report(At(71));

        Assert.Equal(
            Enumerable.Range(1, PlayerRoster.MaxDepartures).Select(id => id.ToString()),
            first.Departed.Select(departed => departed.steam_id)
        );

        roster.Delivered(first, true);
        RosterReport second = roster.Report(At(72));

        Assert.Equal(
            Enumerable.Range(65, 6).Select(id => id.ToString()),
            second.Departed.Select(departed => departed.steam_id)
        );
    }

    [Fact]
    public void DeparturesAreDroppedOnceTheyAreTenMinutesOld()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1")], Start);
        roster.Observe([], At(1));
        roster.Observe([Human("2")], At(2));
        roster.Observe([], At(3));

        Assert.Equal(2, roster.Report(At(1) + PlayerRoster.DepartedTtl).Departed.Count);

        RosterReport later = roster.Report(At(2) + PlayerRoster.DepartedTtl);

        Assert.Equal("2", Assert.Single(later.Departed).steam_id);
    }

    [Fact]
    public async Task ARosterThatFailsToReportDoesNotStopTheSyncs()
    {
        FailingRoster roster = new() { ReportFailures = 1 };
        var (loop, _, _, panel) = Loop(roster);

        await loop.Tick(Start);

        Assert.Empty(panel.Bodies);
        Assert.Equal("roster failed", loop.Status().LastError);

        loop.Request();
        await loop.Tick(At(1));

        Assert.Single(panel.Bodies);
    }

    [Fact]
    public async Task ARosterThatFailsToObserveStillLeavesThePlayersToBeAskedAbout()
    {
        FailingRoster roster = new() { ObserveFails = true };
        var (loop, _, _, panel) = Loop(roster);

        Assert.Throws<InvalidOperationException>(() => loop.Observe([Human("1")], Start));

        await loop.Tick(Start);

        Assert.Equal(
            ["1"],
            panel
                .Bodies[0]
                .GetProperty("steam_ids")
                .EnumerateArray()
                .Select(id => id.GetString()!)
                .ToList()
        );
    }

    [Fact]
    public void ARosterSerializesAsTheApiExpects()
    {
        PlayerSanctionsRequest unknown = new() { steam_ids = ["1"] };
        PlayerSanctionsRequest empty = new() { players = [] };
        PlayerSanctionsRequest known = new()
        {
            players =
            [
                new RosterPlayer
                {
                    steam_id = "76561198000000001",
                    conn = "c1",
                    name = "nyx",
                    ip = null,
                    kills = 12,
                    deaths = 9,
                    assists = 5,
                    headshots = 7,
                    damage = 1480,
                    rounds_played = 16,
                    rounds_won = 10,
                    mvps = 3,
                },
            ],
            departed =
            [
                new DepartedPlayer
                {
                    steam_id = "76561198000000002",
                    conn = "c2",
                    kills = 3,
                    deaths = 4,
                    assists = 1,
                    headshots = 2,
                    damage = 410,
                    rounds_played = 6,
                    rounds_won = 2,
                    mvps = 1,
                },
            ],
        };

        Assert.Contains(
            "\"players\":null",
            JsonSerializer.Serialize(unknown, SanctionsClient.Json)
        );
        Assert.Contains("\"departed\":[]", JsonSerializer.Serialize(unknown, SanctionsClient.Json));
        Assert.Contains("\"players\":[]", JsonSerializer.Serialize(empty, SanctionsClient.Json));
        Assert.Contains(
            "\"players\":[{\"steam_id\":\"76561198000000001\",\"conn\":\"c1\",\"name\":\"nyx\",\"ip\":null,\"kills\":12,\"deaths\":9,\"assists\":5,\"headshots\":7,\"damage\":1480,\"rounds_played\":16,\"rounds_won\":10,\"mvps\":3}]",
            JsonSerializer.Serialize(known, SanctionsClient.Json)
        );
        Assert.Contains(
            "\"departed\":[{\"steam_id\":\"76561198000000002\",\"conn\":\"c2\",\"kills\":3,\"deaths\":4,\"assists\":1,\"headshots\":2,\"damage\":410,\"rounds_played\":6,\"rounds_won\":2,\"mvps\":1}]",
            JsonSerializer.Serialize(known, SanctionsClient.Json)
        );
    }

    [Theory]
    [InlineData("nyx", "nyx")]
    [InlineData("  \u0002[red]nyx[/]\u0001 ", "nyx")]
    [InlineData("[re[red]d]nyx", "nyx")]
    [InlineData(null, "")]
    public void NamesLoseTheirChatFormatting(string? raw, string expected)
    {
        Assert.Equal(expected, PlayerRoster.Name(raw));
    }

    [Fact]
    public void NamesAreCappedWithoutSplittingACharacter()
    {
        Assert.Equal(new string('a', 64), PlayerRoster.Name(new string('a', 80)));

        string name = PlayerRoster.Name(new string('a', 63) + "\U0001F600" + "b");

        Assert.Equal(new string('a', 63), name);
    }

    [Theory]
    [InlineData("203.0.113.24:27005", "203.0.113.24")]
    [InlineData("203.0.113.24", "203.0.113.24")]
    [InlineData("[2001:db8::1]:27015", "2001:db8::1")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    [InlineData("[::ffff:203.0.113.24]:27005", "203.0.113.24")]
    [InlineData("127.0.0.1:27005", null)]
    [InlineData("[::1]:27015", null)]
    [InlineData("loopback", null)]
    [InlineData("0.0.0.0", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void AddressesLoseTheirPort(string? raw, string? expected)
    {
        Assert.Equal(expected, PlayerRoster.Address(raw));
    }

    [Theory]
    [InlineData(1, 2, 2, 3, false, true)]
    [InlineData(1, 1, 2, 2, false, false)]
    [InlineData(1, 1, 2, 2, true, false)]
    [InlineData(1, 2, 2, 2, false, false)]
    [InlineData(1, 2, 2, 2, true, true)]
    public void AKillIsAnotherPlayerOnAnotherTeam(
        int attacker,
        int victim,
        int attackerTeam,
        int victimTeam,
        bool teammatesAreEnemies,
        bool expected
    )
    {
        Assert.Equal(
            expected,
            PlayerRoster.CountsAsKill(
                attacker,
                victim,
                attackerTeam,
                victimTeam,
                teammatesAreEnemies
            )
        );
    }

    [Fact]
    public void CountsEveryConnectedHumanAndNothingElse()
    {
        Assert.True(PlayerRoster.IsConnectedHuman(false, true, false, 76561198000000001));
        Assert.False(PlayerRoster.IsConnectedHuman(false, false, false, 76561198000000001));
        Assert.False(PlayerRoster.IsConnectedHuman(true, true, false, 76561198000000001));
        Assert.False(PlayerRoster.IsConnectedHuman(false, true, true, 76561198000000001));
        Assert.False(PlayerRoster.IsConnectedHuman(false, true, false, 0));
    }

    private const int T = PlayerRoster.TeamT;
    private const int CT = PlayerRoster.TeamCT;

    private static Dictionary<string, int> StatsOf(RosterReport report, string steamId)
    {
        return report.Players!.Single(player => player.steam_id == steamId).stats ?? new();
    }

    private static WeaponTotals? WeaponOf(RosterReport report, string steamId, string weapon)
    {
        return report
            .Players!.Single(player => player.steam_id == steamId)
            .weapons?.GetValueOrDefault(weapon);
    }

    // Player 1 (slot 1, T) kills player 2 (slot 2, CT) with a counted kill.
    private static KillFacts Kill(
        string weapon = "ak47",
        float time = 10f,
        int killer = 1,
        int killerTeam = T,
        int victim = 2,
        int victimTeam = CT
    )
    {
        return new KillFacts(
            Weapon: weapon,
            Time: time,
            KillerSlot: killer,
            KillerTeam: killerTeam,
            VictimSlot: victim,
            VictimTeam: victimTeam
        );
    }

    // Player 1 hits player 2 (slot 2), an enemy, in the chest.
    private static HitFacts Hit(
        string weapon = "ak47",
        int hitGroup = 2,
        int tick = 100,
        int victim = 2
    )
    {
        return new HitFacts(weapon, hitGroup, tick, victim, Enemy: true);
    }

    [Fact]
    public void EachKillTypeCountsOnACountedKill()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);

        roster.Died("2", "1", warmup: false, facts: Kill("knife_t"));
        roster.Died("2", "1", warmup: false, facts: Kill("taser"));
        roster.Died("2", "1", warmup: false, facts: Kill("hegrenade"));
        roster.Died("2", "1", warmup: false, facts: Kill("inferno"));
        roster.Died("2", "1", warmup: false, facts: Kill("awp") with { NoScope = true });
        roster.Died("2", "1", warmup: false, facts: Kill() with { ThruSmoke = true });
        roster.Died("2", "1", warmup: false, facts: Kill() with { Penetrated = 1 });
        roster.Died("2", "1", warmup: false, facts: Kill() with { AttackerBlind = true });
        roster.Died("2", "1", warmup: false, facts: Kill() with { AttackerInAir = true });

        Dictionary<string, int> stats = StatsOf(roster.Report(At(1)), "1");

        Assert.Equal(1, stats[CommunityStatKeys.KnifeKills]);
        Assert.Equal(1, stats[CommunityStatKeys.ZeusKills]);
        Assert.Equal(1, stats[CommunityStatKeys.HeKills]);
        Assert.Equal(1, stats[CommunityStatKeys.MolotovKills]);
        Assert.Equal(1, stats[CommunityStatKeys.NoScopeKills]);
        Assert.Equal(1, stats[CommunityStatKeys.ThruSmokeKills]);
        Assert.Equal(1, stats[CommunityStatKeys.ThruWallKills]);
        Assert.Equal(1, stats[CommunityStatKeys.BlindedKills]);
        Assert.Equal(1, stats[CommunityStatKeys.InAirKills]);
    }

    // The hooks pass no killer for a kill that does not count: a bot's death,
    // a teammate's, or a kill by an unverified player.
    [Fact]
    public void AKillThatDoesNotCountHasNoKillType()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);

        roster.Died(
            "2",
            null,
            warmup: false,
            headshot: true,
            facts: Kill("knife") with
            {
                KillerSlot = -1,
                NoScope = true,
            }
        );

        RosterReport report = roster.Report(At(1));

        Assert.Null(report.Players!.Single(player => player.steam_id == "1").stats);
        Assert.Null(report.Players!.Single(player => player.steam_id == "1").weapons);
    }

    [Fact]
    public void ATeamKillAndASuicideAreCountedAndAreDeaths()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);

        roster.Died(
            "2",
            null,
            warmup: false,
            facts: Kill() with
            {
                KillerSlot = -1,
                TeamKiller = "1",
            }
        );
        roster.Died("1", null, warmup: false, facts: new KillFacts(Suicide: true));
        roster.Died(
            "2",
            null,
            warmup: false,
            facts: Kill() with
            {
                KillerSlot = -1,
                TeamKiller = "bot",
            }
        );

        RosterReport report = roster.Report(At(1));

        Assert.Equal(1, StatsOf(report, "1")[CommunityStatKeys.TeamKills]);
        Assert.Equal(1, StatsOf(report, "1")[CommunityStatKeys.Suicides]);
        Assert.Equal(new Counters(0, 1, 0, 0, 0, 0, 0, 0), CountersOf(report, "1"));
        Assert.Equal(new Counters(0, 2, 0, 0, 0, 0, 0, 0), CountersOf(report, "2"));
        Assert.False(StatsOf(report, "2").ContainsKey(CommunityStatKeys.TeamKills));
    }

    // Killing one's own teammate is a team kill only while teammates are not
    // enemies, and killing oneself never is.
    [Theory]
    [InlineData(1, 2, 2, 2, false, true)]
    [InlineData(1, 2, 2, 2, true, false)]
    [InlineData(1, 2, 2, 3, false, false)]
    [InlineData(1, 1, 2, 2, false, false)]
    public void ATeamKillIsAnotherPlayerOnOnesOwnSide(
        int attacker,
        int victim,
        int attackerTeam,
        int victimTeam,
        bool teammatesAreEnemies,
        bool expected
    )
    {
        Assert.Equal(
            expected,
            PlayerRoster.IsTeamKill(attacker, victim, attackerTeam, victimTeam, teammatesAreEnemies)
        );
    }

    // The hooks pass an assister that is an enemy of the victim, flash or not,
    // and the facts say which it was.
    [Fact]
    public void AFlashAssistIsCountedApartFromAssists()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2"), Human("3")], Start);

        roster.Died(
            "2",
            "1",
            warmup: false,
            assister: "3",
            facts: Kill() with
            {
                AssistedFlash = true,
            }
        );
        roster.Died("2", "1", warmup: false, assister: "3", facts: Kill());
        roster.Died(
            "2",
            "1",
            warmup: false,
            assister: "1",
            facts: Kill() with
            {
                AssistedFlash = true,
            }
        );

        RosterReport report = roster.Report(At(1));

        Assert.Equal(1, CountersOf(report, "3").Assists);
        Assert.Equal(1, StatsOf(report, "3")[CommunityStatKeys.FlashAssists]);
        Assert.False(StatsOf(report, "1").ContainsKey(CommunityStatKeys.FlashAssists));
    }

    [Fact]
    public void KillsAndHeadshotsAreCountedPerWeapon()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);

        roster.Died("2", "1", warmup: false, headshot: true, facts: Kill("ak47"));
        roster.Died("2", "1", warmup: false, facts: Kill("ak47"));
        roster.Died("2", "1", warmup: false, facts: Kill("knife_karambit"));

        RosterReport report = roster.Report(At(1));

        Assert.Equal(2, WeaponOf(report, "1", "ak47")!.kills);
        Assert.Equal(1, WeaponOf(report, "1", "ak47")!.headshots);
        Assert.Equal(1, WeaponOf(report, "1", "knife")!.kills);
        Assert.Equal(0, WeaponOf(report, "1", "knife")!.headshots);
    }

    [Fact]
    public void TheBestStreakIsCountedKillsBetweenDeaths()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);

        roster.Died("2", "1", warmup: false);
        roster.Died("2", "1", warmup: false);
        roster.RoundEnded(T, Teams(("1", T), ("2", CT)), warmup: false);
        roster.RoundStarted();
        roster.Died("2", "1", warmup: false);
        roster.Died("2", "bot", warmup: false);
        roster.Died("1", "bot", warmup: false);
        roster.Died("2", "1", warmup: false);
        roster.Died("1", null, warmup: false, facts: new KillFacts(Suicide: true));
        roster.Died("2", "1", warmup: false);
        roster.Died("2", "1", warmup: false);

        RosterReport report = roster.Report(At(1));

        Assert.Equal(3, StatsOf(report, "1")[CommunityStatKeys.BestStreak]);
        Assert.False(StatsOf(report, "2").ContainsKey(CommunityStatKeys.BestStreak));
    }

    // A death at a bot's hands is no death, but the streak still ends there.
    [Fact]
    public void ADeathAtABotsHandsEndsTheStreakAndIsNoDeath()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);

        roster.Died("2", "1", warmup: false);
        roster.Died("2", "1", warmup: false);
        roster.Died("1", null, warmup: false, facts: new KillFacts(Suicide: true, ByBot: true));
        roster.Died("2", "1", warmup: false);

        RosterReport report = roster.Report(At(1));

        Assert.Equal(2, StatsOf(report, "1")[CommunityStatKeys.BestStreak]);
        Assert.Equal(0, CountersOf(report, "1").Deaths);
        Assert.False(StatsOf(report, "1").ContainsKey(CommunityStatKeys.Suicides));
    }

    [Fact]
    public void ANewMapStartsTheStreakOver()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);
        roster.Died("2", "1", warmup: false);
        roster.Died("2", "1", warmup: false);

        roster.MapEnded(At(1));
        RosterReport final = roster.Report(At(1));
        roster.Delivered(final, true);
        roster.MapStarted(At(2), "de_inferno");
        roster.Observe([Human("1"), Human("2")], At(3));
        roster.Died("2", "1", warmup: false);

        Assert.Equal(2, StatsOf(final, "1")[CommunityStatKeys.BestStreak]);
        Assert.Equal(1, StatsOf(roster.Report(At(4)), "1")[CommunityStatKeys.BestStreak]);
    }

    // Slots 1 and 3 are T with players 1 and 3, slot 2 is CT with player 2.
    [Fact]
    public void KillingATeammatesKillerWithinFiveSecondsIsATrade()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2"), Human("3")], Start);

        roster.Died(
            "3",
            "2",
            warmup: false,
            facts: Kill(time: 10f, killer: 2, killerTeam: CT, victim: 3, victimTeam: T)
        );
        roster.Died(
            "2",
            "1",
            warmup: false,
            facts: Kill(time: 14f, killer: 1, killerTeam: T, victim: 2, victimTeam: CT)
        );

        roster.Died(
            "3",
            "2",
            warmup: false,
            facts: Kill(time: 20f, killer: 2, killerTeam: CT, victim: 3, victimTeam: T)
        );
        roster.Died(
            "2",
            "1",
            warmup: false,
            facts: Kill(time: 26f, killer: 1, killerTeam: T, victim: 2, victimTeam: CT)
        );

        roster.Died(
            "3",
            "2",
            warmup: false,
            facts: Kill(time: 30f, killer: 2, killerTeam: CT, victim: 3, victimTeam: T)
        );
        roster.RoundStarted();
        roster.Died(
            "2",
            "1",
            warmup: false,
            facts: Kill(time: 31f, killer: 1, killerTeam: T, victim: 2, victimTeam: CT)
        );

        Assert.Equal(1, StatsOf(roster.Report(At(1)), "1")[CommunityStatKeys.TradeKills]);
    }

    // Where teammates are enemies there is nobody to avenge.
    [Fact]
    public void ThereAreNoTradesWhereTeammatesAreEnemies()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2"), Human("3")], Start);

        roster.Died(
            "3",
            "2",
            warmup: false,
            facts: Kill(time: 10f, killer: 2, killerTeam: CT, victim: 3, victimTeam: T) with
            {
                TeammatesAreEnemies = true,
            }
        );
        roster.Died(
            "2",
            "1",
            warmup: false,
            facts: Kill(time: 11f, killer: 1, killerTeam: T, victim: 2, victimTeam: CT) with
            {
                TeammatesAreEnemies = true,
            }
        );

        Assert.False(StatsOf(roster.Report(At(1)), "1").ContainsKey(CommunityStatKeys.TradeKills));
    }

    [Fact]
    public void AHitOnAnEnemyIsCountedByHitgroupAndWeapon()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);

        roster.Hurt("1", "2", 27, warmup: false, Hit(hitGroup: 2, tick: 100));
        roster.Hurt("1", "2", 73, warmup: false, Hit(hitGroup: 1, tick: 101));
        roster.Hurt("1", "2", 20, warmup: false, Hit(hitGroup: 8, tick: 102));
        roster.Hurt("1", "2", 5, warmup: false, Hit(hitGroup: 0, tick: 103));
        roster.Hurt("1", "2", 5, warmup: false, Hit(hitGroup: 10, tick: 104));

        RosterReport report = roster.Report(At(1));
        Dictionary<string, int> stats = StatsOf(report, "1");

        Assert.Equal(5, stats[CommunityStatKeys.Hits]);
        Assert.Equal(1, stats[CommunityStatKeys.HitsChest]);
        Assert.Equal(1, stats[CommunityStatKeys.HitsHead]);
        Assert.Equal(1, stats[CommunityStatKeys.HitsNeck]);
        Assert.Equal(5, WeaponOf(report, "1", "ak47")!.hits);
        Assert.Equal(130, WeaponOf(report, "1", "ak47")!.damage);
        Assert.Equal(130, CountersOf(report, "1").Damage);
        Assert.Equal(130, StatsOf(report, "2")[CommunityStatKeys.DamageTaken]);
    }

    // A shotgun's pellets each report a hit, all in one tick.
    [Fact]
    public void PelletsInOneTickAreOneHitOnEachVictim()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2"), Human("3")], Start);

        roster.Hurt("1", "2", 20, warmup: false, Hit("nova", tick: 100, victim: 2));
        roster.Hurt("1", "3", 20, warmup: false, Hit("nova", tick: 100, victim: 3));
        roster.Hurt("1", "2", 20, warmup: false, Hit("nova", tick: 100, victim: 2));
        roster.Hurt("1", "2", 20, warmup: false, Hit("nova", tick: 164, victim: 2));

        RosterReport report = roster.Report(At(1));

        Assert.Equal(3, StatsOf(report, "1")[CommunityStatKeys.Hits]);
        Assert.Equal(3, WeaponOf(report, "1", "nova")!.hits);
        Assert.Equal(80, WeaponOf(report, "1", "nova")!.damage);
    }

    // A shot that hits a bot is taken back, once however many pellets hit;
    // a miss stays a shot, and a hit on a human beside a bot stays a hit.
    [Fact]
    public void AShotThatHitsABotIsTakenBack()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);

        roster.Fired("1", "weapon_ak47", warmup: false);
        roster.Fired("1", "weapon_ak47", warmup: false);
        roster.Fired("1", "weapon_ak47", warmup: false);
        roster.HitABot("1", "ak47", 100, warmup: false);
        roster.Hurt("1", "2", 27, warmup: false, Hit(tick: 101));

        roster.Fired("1", "weapon_nova", warmup: false);
        roster.HitABot("1", "nova", 200, warmup: false);
        roster.HitABot("1", "nova", 200, warmup: false);

        RosterReport report = roster.Report(At(1));
        Dictionary<string, int> stats = StatsOf(report, "1");

        Assert.Equal(2, stats[CommunityStatKeys.Shots]);
        Assert.Equal(1, stats[CommunityStatKeys.Hits]);
        Assert.Equal(2, WeaponOf(report, "1", "ak47")!.shots);
        Assert.Equal(1, WeaponOf(report, "1", "ak47")!.hits);
        Assert.Equal(27, CountersOf(report, "1").Damage);
        Assert.Null(WeaponOf(report, "1", "nova"));
    }

    // A shotgun's pellets that hit a bot and a human in one tick are a shot
    // that hit, whichever the event names first.
    [Fact]
    public void AShotThatHitsABotAndAHumanStaysAShot()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);

        roster.Fired("1", "weapon_nova", warmup: false);
        roster.HitABot("1", "nova", 100, warmup: false);
        roster.Hurt("1", "2", 20, warmup: false, Hit("nova", tick: 100));
        roster.Fired("1", "weapon_nova", warmup: false);
        roster.Hurt("1", "2", 20, warmup: false, Hit("nova", tick: 200));
        roster.HitABot("1", "nova", 200, warmup: false);

        RosterReport report = roster.Report(At(1));

        Assert.Equal(2, StatsOf(report, "1")[CommunityStatKeys.Shots]);
        Assert.Equal(2, WeaponOf(report, "1", "nova")!.shots);
        Assert.Equal(2, WeaponOf(report, "1", "nova")!.hits);
    }

    // Nothing is taken back that was never counted, nor in warmup.
    [Fact]
    public void AHitOnABotNeverTakesBackMoreThanTheShots()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1")], Start);

        roster.HitABot("1", "deagle", 100, warmup: false);
        roster.Fired("1", "weapon_deagle", warmup: false);
        roster.HitABot("1", "deagle", 101, warmup: true);
        roster.HitABot("1", "knife", 102, warmup: false);
        roster.HitABot(null, "deagle", 103, warmup: false);

        RosterReport report = roster.Report(At(1));

        Assert.Equal(1, StatsOf(report, "1")[CommunityStatKeys.Shots]);
        Assert.Equal(1, WeaponOf(report, "1", "deagle")!.shots);
    }

    // Only bullets hit: a knife, a grenade or a fire does damage but is no hit.
    [Fact]
    public void OnlyAFirearmHits()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);

        roster.Hurt("1", "2", 40, warmup: false, Hit("knife", tick: 100));
        roster.Hurt("1", "2", 30, warmup: false, Hit("hegrenade", tick: 101));
        roster.Hurt("1", "2", 8, warmup: false, Hit("inferno", tick: 102));
        roster.Hurt("1", "2", 7, warmup: false, Hit("molotov", tick: 103));
        roster.Fired("1", "weapon_knife", warmup: false);
        roster.Fired("1", "weapon_hegrenade", warmup: false);

        RosterReport report = roster.Report(At(1));
        Dictionary<string, int> stats = StatsOf(report, "1");

        Assert.False(stats.ContainsKey(CommunityStatKeys.Hits));
        Assert.False(stats.ContainsKey(CommunityStatKeys.Shots));
        Assert.Equal(30, stats[CommunityStatKeys.HeDamage]);
        Assert.Equal(15, stats[CommunityStatKeys.MolotovDamage]);
        Assert.Equal(85, CountersOf(report, "1").Damage);
        Assert.Equal(40, WeaponOf(report, "1", "knife")!.damage);
        Assert.Equal(30, WeaponOf(report, "1", "hegrenade")!.damage);
    }

    // A teammate's or one's own grenade is team damage, as on the match side;
    // it is no damage dealt, no damage taken and no hit.
    [Fact]
    public void TeamAndSelfDamageCountAsTeamDamageOnly()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);
        HitFacts team = new("ak47", 2, 100, 2, Enemy: false, Team: true);
        HitFacts self = new("hegrenade", 0, 101, 1, Enemy: false, Team: true);

        roster.Hurt("1", "2", 25, warmup: false, team);
        roster.Hurt("1", "1", 40, warmup: false, self);

        RosterReport report = roster.Report(At(1));
        Dictionary<string, int> stats = StatsOf(report, "1");

        Assert.Equal(65, stats[CommunityStatKeys.TeamDamage]);
        Assert.False(stats.ContainsKey(CommunityStatKeys.Hits));
        Assert.False(stats.ContainsKey(CommunityStatKeys.HeDamage));
        Assert.False(stats.ContainsKey(CommunityStatKeys.DamageTaken));
        Assert.False(StatsOf(report, "2").ContainsKey(CommunityStatKeys.DamageTaken));
        Assert.Equal(0, CountersOf(report, "1").Damage);
        Assert.Null(report.Players!.Single(player => player.steam_id == "1").weapons);
    }

    // An unverified enemy still hurts; the world and oneself do not count as
    // damage taken.
    [Fact]
    public void DamageTakenIsWhatAnyEnemyDealt()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("2")], Start);

        roster.Hurt(
            null,
            "2",
            30,
            warmup: false,
            new HitFacts("ak47", 2, 100, 2, Enemy: true)
        );
        roster.Hurt(null, "2", 12, warmup: false, new HitFacts("", 0, 101, 2, Enemy: false));
        roster.Hurt(
            "2",
            "2",
            40,
            warmup: false,
            new HitFacts("hegrenade", 0, 102, 2, Enemy: false, Team: true)
        );

        Assert.Equal(30, StatsOf(roster.Report(At(1)), "2")[CommunityStatKeys.DamageTaken]);
    }

    // A hit on a teammate is a hit on nobody the player was aiming for.
    [Fact]
    public void AHitOnATeammateIsNoHit()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1")], Start);

        roster.Hurt(
            "1",
            null,
            25,
            warmup: false,
            new HitFacts("ak47", 2, 100, 5, Enemy: false)
        );

        Assert.Null(roster.Report(At(1)).Players!.Single().stats);
    }

    [Theory]
    [InlineData(2.5f, false, 1, 0, 2500)]
    [InlineData(1.01f, false, 1, 0, 1010)]
    [InlineData(1.0f, false, 0, 0, 0)]
    [InlineData(0.4f, false, 0, 0, 0)]
    [InlineData(2.5f, true, 0, 1, 0)]
    [InlineData(1.0f, true, 0, 0, 0)]
    public void AFlashLongerThanASecondIsCountedByTeam(
        float seconds,
        bool sameTeam,
        int enemies,
        int team,
        int durationMs
    )
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);

        roster.Blinded("1", "2", seconds, sameTeam, warmup: false);

        Dictionary<string, int> stats = StatsOf(roster.Report(At(1)), "1");

        Assert.Equal(enemies, stats.GetValueOrDefault(CommunityStatKeys.EnemiesFlashed));
        Assert.Equal(team, stats.GetValueOrDefault(CommunityStatKeys.TeamFlashed));
        Assert.Equal(durationMs, stats.GetValueOrDefault(CommunityStatKeys.FlashDurationMs));
    }

    // Flashing oneself is a team flash, as on the match side.
    [Fact]
    public void FlashingOneselfIsATeamFlash()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1")], Start);

        roster.Blinded("1", "1", 3f, sameTeam: true, warmup: false);
        roster.Blinded("bot", "1", 3f, sameTeam: false, warmup: false);
        roster.Blinded(null, "1", 3f, sameTeam: false, warmup: false);

        Dictionary<string, int> stats = StatsOf(roster.Report(At(1)), "1");

        Assert.Equal(1, stats[CommunityStatKeys.TeamFlashed]);
        Assert.False(stats.ContainsKey(CommunityStatKeys.EnemiesFlashed));
    }

    [Fact]
    public void ShotsAreFirearmShotsByRosterPlayers()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1")], Start);

        roster.Fired("1", "weapon_ak47", warmup: false);
        roster.Fired("1", "weapon_ak47", warmup: false);
        roster.Fired("1", "weapon_deagle", warmup: false);
        roster.Fired("1", "weapon_taser", warmup: false);
        roster.Fired("bot", "weapon_ak47", warmup: false);
        roster.Fired(null, "weapon_ak47", warmup: false);

        RosterReport report = roster.Report(At(1));

        Assert.Equal(3, StatsOf(report, "1")[CommunityStatKeys.Shots]);
        Assert.Equal(2, WeaponOf(report, "1", "ak47")!.shots);
        Assert.Equal(1, WeaponOf(report, "1", "deagle")!.shots);
        Assert.Null(WeaponOf(report, "1", "taser"));
    }

    // A silenced gun is one gun whether its silencer is on or off.
    [Fact]
    public void ASilencedGunKeepsOneKeyWithItsSilencerOff()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);

        roster.Fired("1", "weapon_m4a1_silencer", warmup: false);
        roster.Died("2", "1", warmup: false, facts: Kill("weapon_m4a1_silencer_off"));

        RosterReport report = roster.Report(At(1));

        Assert.Equal(1, WeaponOf(report, "1", "m4a1_silencer")!.shots);
        Assert.Equal(1, WeaponOf(report, "1", "m4a1_silencer")!.kills);
        Assert.Null(WeaponOf(report, "1", "m4a1_silencer_off"));
    }

    [Fact]
    public void ObjectivesGoToRosterPlayersOnly()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1")], Start);

        roster.Objective("1", CommunityStatKeys.BombPlants, warmup: false);
        roster.Objective("1", CommunityStatKeys.BombDefuses, warmup: false);
        roster.Objective("1", CommunityStatKeys.HostageRescues, warmup: false);
        roster.Objective("1", CommunityStatKeys.HostageRescues, warmup: false);
        roster.Objective("bot", CommunityStatKeys.BombPlants, warmup: false);
        roster.Objective(null, CommunityStatKeys.BombPlants, warmup: false);

        Dictionary<string, int> stats = StatsOf(roster.Report(At(1)), "1");

        Assert.Equal(1, stats[CommunityStatKeys.BombPlants]);
        Assert.Equal(1, stats[CommunityStatKeys.BombDefuses]);
        Assert.Equal(2, stats[CommunityStatKeys.HostageRescues]);
    }

    [Fact]
    public void NoDetailedStatIsCountedInWarmup()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2"), Human("3")], Start);

        roster.Died(
            "2",
            "1",
            warmup: true,
            assister: "3",
            facts: Kill("knife") with
            {
                AssistedFlash = true,
            }
        );
        roster.Died(
            "2",
            null,
            warmup: true,
            facts: Kill() with
            {
                KillerSlot = -1,
                TeamKiller = "1",
            }
        );
        roster.Died("1", null, warmup: true, facts: new KillFacts(Suicide: true));
        roster.Hurt("1", "2", 50, warmup: true, Hit());
        roster.Hurt(
            "1",
            "1",
            50,
            warmup: true,
            new HitFacts("hegrenade", 0, 1, 1, Enemy: false, Team: true)
        );
        roster.Fired("1", "weapon_ak47", warmup: true);
        roster.Blinded("1", "2", 3f, sameTeam: false, warmup: true);
        roster.Objective("1", CommunityStatKeys.BombPlants, warmup: true);

        RosterReport report = roster.Report(At(1));

        Assert.All(report.Players!, player => Assert.Null(player.stats));
        Assert.All(report.Players!, player => Assert.Null(player.weapons));
    }

    // The report is serialized once the lock is released, while the game
    // thread goes on counting.
    [Fact]
    public void TheReportHoldsCopiesOfTheCounters()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);
        roster.Died("2", "1", warmup: false, facts: Kill("knife"));
        roster.Fired("1", "weapon_ak47", warmup: false);

        RosterReport first = roster.Report(At(1));
        roster.Died("2", "1", warmup: false, facts: Kill("knife"));
        roster.Fired("1", "weapon_ak47", warmup: false);
        first.Players!.Single(player => player.steam_id == "1").stats![CommunityStatKeys.Shots] =
            99;
        RosterReport second = roster.Report(At(2));

        Assert.Equal(1, StatsOf(first, "1")[CommunityStatKeys.KnifeKills]);
        Assert.Equal(1, WeaponOf(first, "1", "ak47")!.shots);
        Assert.Equal(2, StatsOf(second, "1")[CommunityStatKeys.KnifeKills]);
        Assert.Equal(2, StatsOf(second, "1")[CommunityStatKeys.Shots]);
        Assert.Equal(2, WeaponOf(second, "1", "ak47")!.shots);
    }

    [Fact]
    public void ANewConnectionStartsWithNoStatsAndADepartureKeepsItsOwn()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);
        roster.Died("2", "1", warmup: false, facts: Kill("knife"));
        roster.Fired("1", "weapon_ak47", warmup: false);

        roster.Observe([Human("2")], At(1));
        roster.Observe([Human("1"), Human("2")], At(2));
        roster.Fired("1", "weapon_ak47", warmup: false);

        RosterReport report = roster.Report(At(3));
        DepartedPlayer departed = Assert.Single(report.Departed);

        Assert.Equal(
            new Dictionary<string, int>
            {
                [CommunityStatKeys.Shots] = 1,
                [CommunityStatKeys.KnifeKills] = 1,
                [CommunityStatKeys.BestStreak] = 1,
            },
            departed.stats
        );
        Assert.Equal(1, departed.weapons!["knife"].kills);
        Assert.Equal(1, departed.weapons!["ak47"].shots);
        Assert.Equal(
            new Dictionary<string, int> { [CommunityStatKeys.Shots] = 1 },
            StatsOf(report, "1")
        );
        Assert.Equal(
            ["ak47"],
            report.Players!.Single(player => player.steam_id == "1").weapons!.Keys
        );
    }

    [Fact]
    public void AConnectionKeepsAtMostFortyEightWeapons()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);

        for (int weapon = 0; weapon < 60; weapon++)
        {
            roster.Died("2", "1", warmup: false, facts: Kill($"weapon_gun{weapon}"));
        }

        RosterReport report = roster.Report(At(1));

        Assert.Equal(
            PlayerRoster.MaxWeapons,
            report.Players!.Single(player => player.steam_id == "1").weapons!.Count
        );
        Assert.Equal(60, CountersOf(report, "1").Kills);
    }

    [Fact]
    public void TheDetailedStatsSerializeWithoutZerosAndOnlyWhenThereAreAny()
    {
        RosterPlayer plain = new()
        {
            steam_id = "76561198000000001",
            conn = "c1",
            name = "nyx",
            kills = 2,
        };
        RosterPlayer detailed = new()
        {
            steam_id = "76561198000000001",
            conn = "c1",
            name = "nyx",
            kills = 2,
            stats = new() { ["shots"] = 412, ["best_streak"] = 5 },
            weapons = new()
            {
                ["ak47"] = new WeaponTotals
                {
                    kills = 9,
                    shots = 300,
                    hits = 95,
                },
            },
        };
        DepartedPlayer departed = new()
        {
            steam_id = "76561198000000002",
            conn = "c2",
            stats = new() { ["hits"] = 3 },
        };

        string plainJson = JsonSerializer.Serialize(plain, SanctionsClient.Json);

        Assert.DoesNotContain("stats", plainJson);
        Assert.DoesNotContain("weapons", plainJson);
        Assert.EndsWith("\"rounds_won\":0,\"mvps\":0}", plainJson);
        Assert.EndsWith(
            "\"mvps\":0,\"stats\":{\"shots\":412,\"best_streak\":5},\"weapons\":{\"ak47\":{\"kills\":9,\"shots\":300,\"hits\":95}}}",
            JsonSerializer.Serialize(detailed, SanctionsClient.Json)
        );
        Assert.EndsWith(
            "\"mvps\":0,\"stats\":{\"hits\":3}}",
            JsonSerializer.Serialize(departed, SanctionsClient.Json)
        );
    }

    // Zeros are left out of what the roster reports, so a stat the player has
    // not earned is not sent at all.
    [Fact]
    public void TheRosterLeavesOutStatsThatAreZero()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);

        roster.Died("2", "1", warmup: false, facts: Kill());

        RosterReport report = roster.Report(At(1));
        RosterPlayer player = report.Players!.Single(player => player.steam_id == "1");

        Assert.Equal(
            new Dictionary<string, int> { [CommunityStatKeys.BestStreak] = 1 },
            player.stats
        );
        Assert.Equal(1, player.weapons!["ak47"].kills);
        Assert.Null(report.Players!.Single(player => player.steam_id == "2").stats);
        Assert.Null(report.Players!.Single(player => player.steam_id == "2").weapons);
    }

    // Where players respawn, killing the same killer again within the window
    // is no second trade of the same teammate.
    [Fact]
    public void ARespawnedKillerIsTradedOnlyOnce()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2"), Human("3")], Start);

        roster.Died(
            "3",
            "2",
            warmup: false,
            facts: Kill(time: 10f, killer: 2, killerTeam: CT, victim: 3, victimTeam: T)
        );
        roster.Died(
            "2",
            "1",
            warmup: false,
            facts: Kill(time: 11f, killer: 1, killerTeam: T, victim: 2, victimTeam: CT)
        );
        roster.Died(
            "2",
            "1",
            warmup: false,
            facts: Kill(time: 13f, killer: 1, killerTeam: T, victim: 2, victimTeam: CT)
        );

        Assert.Equal(1, StatsOf(roster.Report(At(1)), "1")[CommunityStatKeys.TradeKills]);
    }

    // A killer who dies to the world and respawns is no longer the killer of
    // the teammate they killed before.
    [Fact]
    public void AKillerWhoDiedToTheWorldIsNotTraded()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2"), Human("3")], Start);

        roster.Died(
            "3",
            "2",
            warmup: false,
            facts: Kill(time: 10f, killer: 2, killerTeam: CT, victim: 3, victimTeam: T)
        );
        roster.Died(
            "2",
            null,
            warmup: false,
            facts: new KillFacts(
                Weapon: "world",
                Time: 11f,
                VictimSlot: 2,
                VictimTeam: CT,
                Suicide: true
            )
        );
        roster.Died(
            "2",
            "1",
            warmup: false,
            facts: Kill(time: 12f, killer: 1, killerTeam: T, victim: 2, victimTeam: CT)
        );

        Assert.False(StatsOf(roster.Report(At(1)), "1").ContainsKey(CommunityStatKeys.TradeKills));
    }

    // Where teammates are enemies there is no team, so hurting or flashing
    // oneself is no team damage or team flash either.
    [Fact]
    public void ThereAreNoTeamStatsWhereTeammatesAreEnemies()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1")], Start);

        roster.Hurt(
            "1",
            "1",
            40,
            warmup: false,
            new HitFacts(
                "hegrenade",
                0,
                100,
                1,
                Enemy: false,
                Team: true,
                TeammatesAreEnemies: true
            )
        );
        roster.Blinded("1", "1", 3f, sameTeam: true, warmup: false, teammatesAreEnemies: true);

        Assert.Null(roster.Report(At(1)).Players!.Single().stats);
    }

    [Fact]
    public void AFlashOnAnotherPlayerWhereTeammatesAreEnemiesIsAnEnemyFlash()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1"), Human("2")], Start);

        roster.Blinded("1", "2", 3f, sameTeam: false, warmup: false, teammatesAreEnemies: true);

        Assert.Equal(1, StatsOf(roster.Report(At(1)), "1")[CommunityStatKeys.EnemiesFlashed]);
    }

    // The bomb is no attacker, but dying to it is no suicide.
    [Theory]
    [InlineData(2, 2, "hegrenade", true)]
    [InlineData(null, 2, "world", true)]
    [InlineData(null, 2, "trigger_hurt", true)]
    [InlineData(null, 2, "", true)]
    [InlineData(null, 2, "planted_c4", false)]
    [InlineData(null, 2, "weapon_planted_c4", false)]
    [InlineData(1, 2, "ak47", false)]
    [InlineData(1, 2, "planted_c4", false)]
    public void ASuicideIsADeathByOnesOwnHandOrTheWorldsButNotTheBombs(
        int? attacker,
        int victim,
        string weapon,
        bool expected
    )
    {
        Assert.Equal(expected, PlayerRoster.IsSuicide(attacker, victim, weapon));
    }

    [Fact]
    public void ADeathToTheBombIsADeathButNoSuicide()
    {
        PlayerRoster roster = new();
        roster.Observe([Human("1")], Start);

        roster.Died(
            "1",
            null,
            warmup: false,
            facts: new KillFacts(
                Weapon: "planted_c4",
                VictimSlot: 1,
                VictimTeam: CT,
                Suicide: PlayerRoster.IsSuicide(null, 1, "planted_c4")
            )
        );

        RosterReport report = roster.Report(At(1));

        Assert.Equal(1, CountersOf(report, "1").Deaths);
        Assert.Null(report.Players!.Single().stats);
    }
}
