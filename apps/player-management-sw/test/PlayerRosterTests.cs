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
        roster.Died(null, "1");
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
        await loop.Tick(Start + SanctionSyncLoop.Interval);

        Assert.Null(panel.Players(1));
        Assert.Empty(panel.Departed(1));

        roster.MapStarted(At(40));
        loop.Observe([Human("1")], At(41));
        loop.Observe([Human("1"), Human("2")], At(50));
        await loop.Tick(At(50) + PlayerRoster.Debounce);

        Assert.Equal(["1", "2"], panel.SteamIds(2));
        Assert.Equal(conns, panel.Conns(2));
        Assert.Empty(panel.Departed(2));
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
        roster.MapStarted(At(2));

        for (int second = 3; second <= 20; second++)
        {
            loop.Observe([], At(second));
            await loop.Tick(At(second));
        }

        loop.Observe([Human("1")], At(21));
        loop.Observe([Human("1"), Human("2")], At(25));
        await loop.Tick(At(25) + PlayerRoster.Debounce);
        Assert.Single(panel.Bodies);

        loop.Observe([Human("1"), Human("2")], Start + SanctionSyncLoop.Interval);
        await loop.Tick(Start + SanctionSyncLoop.Interval);

        Assert.Equal(conns, panel.Conns(1));
        Assert.Empty(panel.Departed(1));
    }

    [Fact]
    public async Task WhoeverHasNotComeBackWhenTheHoldSettlesHasLeft()
    {
        var (loop, roster, _, panel) = Loop();
        loop.Observe([Human("1"), Human("2")], Start);
        await loop.Tick(Start);

        roster.MapEnded(At(1));
        roster.MapStarted(At(5));
        loop.Observe([Human("1")], At(6));

        DateTimeOffset settled = At(6) + PlayerRoster.Settle;
        loop.Observe([Human("1")], settled - TimeSpan.FromSeconds(1));
        await loop.Tick(settled - TimeSpan.FromSeconds(1));
        Assert.Null(panel.Players(1));

        loop.Observe([Human("1")], settled);
        await loop.Tick(settled + PlayerRoster.Debounce);

        Assert.Equal(["1"], panel.SteamIds(2));
        Assert.Equal([("2", 0, 0)], panel.Departed(2));
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
        roster.MapStarted(At(2));

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
        roster.Died("2", "1");
        roster.Died("2", "1");
        roster.Died("1", null);
        roster.Died(null, "bot");
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
        roster.Died("2", "1");
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

        roster.Died(null, "1");
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

    [Fact]
    public async Task ASyncWithoutARosterDoesNotSettleDepartures()
    {
        var (loop, roster, _, panel) = Loop();
        loop.Observe([Human("1"), Human("2")], Start);
        await loop.Tick(Start);

        loop.Observe([Human("1")], At(1));
        roster.MapEnded(At(2));
        await loop.Tick(At(1) + PlayerRoster.Debounce);

        Assert.Null(panel.Players(1));
        Assert.Equal([("2", 0, 0)], panel.Departed(1));

        roster.MapStarted(At(3));
        loop.Observe([Human("1")], At(4));
        await loop.Tick(At(4) + PlayerRoster.Debounce);

        DateTimeOffset next = At(4) + PlayerRoster.Debounce + SanctionSyncLoop.Interval;
        loop.Observe([Human("1")], next);
        await loop.Tick(next);

        Assert.Equal(["1"], panel.SteamIds(2));
        Assert.Equal([("2", 0, 0)], panel.Departed(2));
        Assert.Empty(panel.Departed(3));
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
        roster.Died(null, "1");
        roster.Observe([], At(1));
        roster.Observe([Human("1")], At(2));
        roster.Died(null, "1");
        roster.Died(null, "1");
        roster.Died("1", null);
        roster.Observe([], At(3));

        RosterReport report = roster.Report(At(4));

        Assert.Equal(
            [("1", 1, 0), ("1", 2, 1)],
            report.Departed.Select(departed => (departed.steam_id, departed.kills, departed.deaths))
        );
        Assert.NotEqual(report.Departed[0].conn, report.Departed[1].conn);
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
            "\"players\":[{\"steam_id\":\"76561198000000001\",\"conn\":\"c1\",\"name\":\"nyx\",\"ip\":null,\"kills\":12,\"deaths\":9}]",
            JsonSerializer.Serialize(known, SanctionsClient.Json)
        );
        Assert.Contains(
            "\"departed\":[{\"steam_id\":\"76561198000000002\",\"conn\":\"c2\",\"kills\":3,\"deaths\":4}]",
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
}
