using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FiveStack.Entities.PlayerManagement;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

public class SanctionSyncLoopTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private sealed class Panel : HttpMessageHandler
    {
        public readonly List<List<string>> Asked = new();
        public int AccessFetches;
        public Func<List<string>, Task<HttpResponseMessage>> Answer = _ =>
            Task.FromResult(Sanctions());
        public Func<HttpResponseMessage> AccessAnswer = () => AccessList("v1");

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.Method == HttpMethod.Get)
            {
                AccessFetches++;

                return AccessAnswer();
            }

            using JsonDocument body = JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync(cancellationToken)
            );
            List<string> steamIds = body
                .RootElement.GetProperty("steam_ids")
                .EnumerateArray()
                .Select(steamId => steamId.GetString()!)
                .ToList();

            Asked.Add(steamIds);

            return await Answer(steamIds);
        }

        public static HttpResponseMessage Sanctions(string json = "[]", string? access = null)
        {
            return Ok(
                access == null
                    ? $"{{\"sanctions\":{json}}}"
                    : $"{{\"sanctions\":{json},\"access\":{access}}}"
            );
        }

        public static string Access(string version, params string[] denied)
        {
            return JsonSerializer.Serialize(
                new
                {
                    restricted = version != "open",
                    version,
                    denied,
                    message = (string?)null,
                }
            );
        }

        public static HttpResponseMessage AccessList(string version, params string[] steamIds)
        {
            return Ok(
                JsonSerializer.Serialize(
                    new
                    {
                        restricted = true,
                        version,
                        steam_ids = steamIds,
                    }
                )
            );
        }

        private static HttpResponseMessage Ok(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static ObservedPlayer Human(string steamId, bool verified = true)
    {
        return new ObservedPlayer(steamId, $"player {steamId}", null, verified);
    }

    private static PlayerManagementSettings Connected()
    {
        return new PlayerManagementSettings
        {
            API_DOMAIN = "https://api.example.com",
            SERVER_ID = "11111111-1111-1111-1111-111111111111",
            SERVER_API_PASSWORD = "secret",
        };
    }

    private static (
        SanctionSyncLoop Loop,
        SanctionBook Book,
        Panel Panel,
        List<string> Warnings
    ) Loop(Func<PlayerManagementSettings>? settings = null)
    {
        var (loop, book, _, panel, warnings) = WithAccess(settings);

        return (loop, book, panel, warnings);
    }

    private static (
        SanctionSyncLoop Loop,
        SanctionBook Book,
        ServerAccessBook Access,
        Panel Panel,
        List<string> Warnings
    ) WithAccess(Func<PlayerManagementSettings>? settings = null)
    {
        Panel panel = new();
        SanctionBook book = new();
        ServerAccessBook access = new();
        List<string> warnings = new();

        SanctionSyncLoop loop = new(
            book,
            access,
            new PlayerRoster(),
            new SanctionsClient(new HttpClient(panel)),
            settings ?? Connected,
            "0.0.9",
            "swiftlys2",
            warnings.Add,
            _ => { }
        );

        return (loop, book, access, panel, warnings);
    }

    // An empty server is exactly when nothing else would call the panel, and
    // the call is what shows the plugin as active.
    [Fact]
    public async Task AnEmptyServerStillSyncs()
    {
        var (loop, _, panel, _) = Loop();

        await loop.Tick(Start);

        List<string> asked = Assert.Single(panel.Asked);
        Assert.Empty(asked);
    }

    [Fact]
    public async Task ItWaitsTheIntervalBetweenSyncs()
    {
        var (loop, _, panel, _) = Loop();

        await loop.Tick(Start);
        await loop.Tick(Start.AddSeconds(10));
        Assert.Single(panel.Asked);

        await loop.Tick(Start + SanctionSyncLoop.Interval);
        Assert.Equal(2, panel.Asked.Count);
    }

    [Fact]
    public async Task ARequestSyncsWithoutWaitingTheInterval()
    {
        var (loop, _, panel, _) = Loop();

        await loop.Tick(Start);
        loop.Request();
        await loop.Tick(Start.AddSeconds(1));

        Assert.Equal(2, panel.Asked.Count);
    }

    [Fact]
    public async Task ItAsksAboutThePlayersPresentAndThoseJoining()
    {
        var (loop, book, panel, _) = Loop();
        loop.Observe([Human("1"), Human("2"), Human("1")], Start);
        book.Joined("3");

        await loop.Tick(Start);

        Assert.Equal(["1", "2", "3"], panel.Asked[0]);
    }

    // A ban has to reach a player before Steam has verified them, so the
    // roster's verified-only rule must not narrow who is asked about.
    [Fact]
    public async Task AnUnverifiedPlayerIsStillAskedAbout()
    {
        var (loop, _, panel, _) = Loop();
        loop.Observe([Human("1"), Human("2", verified: false)], Start);

        await loop.Tick(Start);

        Assert.Equal(["1", "2"], panel.Asked[0]);
    }

    // The bug this guards: a player banned and kicked here keeps that ban in
    // the cache, is unbanned while away, and must not be kicked again on the
    // way back in by the stale copy.
    [Fact]
    public async Task ALiftedBanIsNotEnforcedOnARejoiningPlayer()
    {
        var (loop, book, _, _) = Loop();
        book.Record(["1"], [new PlayerSanction { steam_id = "1", type = "ban" }]);

        book.Joined("1");
        Assert.True(book.IsAwaiting("1"));

        await loop.Tick(Start);

        Assert.False(book.IsAwaiting("1"));
        Assert.False(book.StateFor("1", Start).IsBanned);
    }

    [Fact]
    public async Task AnUnreachablePanelFallsBackToTheCacheAndSaysSoOnce()
    {
        var (loop, book, panel, warnings) = Loop();
        panel.Answer = _ => throw new HttpRequestException("connection refused");
        book.Record(["1"], [new PlayerSanction { steam_id = "1", type = "ban" }]);
        book.Joined("1");

        await loop.Tick(Start);
        await loop.Tick(Start + SanctionSyncLoop.Interval);

        Assert.False(book.IsAwaiting("1"));
        Assert.True(book.StateFor("1", Start).IsBanned);
        Assert.Equal("connection refused", loop.Status().LastError);
        Assert.Single(warnings);
    }

    [Fact]
    public async Task ASyncInFlightIsNotDoubledAndTheAskIsKeptForTheNext()
    {
        var (loop, _, panel, _) = Loop();
        TaskCompletionSource<HttpResponseMessage> answer = new();
        panel.Answer = _ => answer.Task;

        Task first = loop.Tick(Start);
        loop.Request();
        await loop.Tick(Start.AddSeconds(1));
        Assert.Single(panel.Asked);

        answer.SetResult(Panel.Sanctions());
        await first;

        panel.Answer = _ => Task.FromResult(Panel.Sanctions());
        await loop.Tick(Start.AddSeconds(2));
        Assert.Equal(2, panel.Asked.Count);
    }

    [Fact]
    public async Task AnUnconfiguredServerNeverCallsThePanelNorHoldsPlayersBack()
    {
        var (loop, book, panel, _) = Loop(() => new PlayerManagementSettings());
        book.Joined("1");

        await loop.Tick(Start);

        Assert.Empty(panel.Asked);
        Assert.False(book.IsAwaiting("1"));
    }

    [Fact]
    public async Task ASuccessfulSyncIsReported()
    {
        var (loop, _, _, _) = Loop();

        await loop.Tick(Start);

        Assert.Equal((Start, (string?)null), loop.Status());
    }

    [Fact]
    public async Task ANewAccessVersionFetchesTheListExactlyOnce()
    {
        var (loop, _, access, panel, _) = WithAccess();
        panel.Answer = _ => Task.FromResult(Panel.Sanctions(access: Panel.Access("v1")));
        panel.AccessAnswer = () => Panel.AccessList("v1", "1");

        await loop.Tick(Start);

        Assert.Equal(1, panel.AccessFetches);
        Assert.Equal(eServerAccess.Allowed, access.Decide("1"));
        Assert.Equal(eServerAccess.Denied, access.Decide("2"));

        panel.Answer = _ => Task.FromResult(Panel.Sanctions(access: Panel.Access("v2")));
        panel.AccessAnswer = () => Panel.AccessList("v2", "2");

        await loop.Tick(Start + SanctionSyncLoop.Interval);

        Assert.Equal(2, panel.AccessFetches);
        Assert.Equal(eServerAccess.Denied, access.Decide("1"));
        Assert.Equal(eServerAccess.Allowed, access.Decide("2"));
    }

    [Fact]
    public async Task TheSameAccessVersionIsNotFetchedAgain()
    {
        var (loop, _, _, panel, _) = WithAccess();
        panel.Answer = _ => Task.FromResult(Panel.Sanctions(access: Panel.Access("v1")));

        await loop.Tick(Start);
        await loop.Tick(Start + SanctionSyncLoop.Interval);
        loop.Request();
        await loop.Tick(Start + SanctionSyncLoop.Interval + TimeSpan.FromSeconds(1));

        Assert.Equal(3, panel.Asked.Count);
        Assert.Equal(1, panel.AccessFetches);
    }

    [Fact]
    public async Task TheDeniedPlayersSurfaceToThePlugin()
    {
        var (loop, _, access, panel, _) = WithAccess();
        loop.Observe([Human("1"), Human("2")], Start);
        panel.Answer = _ => Task.FromResult(Panel.Sanctions(access: Panel.Access("v1", "2")));

        await loop.Tick(Start);

        Assert.True(access.IsDenied("2"));
        Assert.False(access.IsDenied("1"));

        panel.Answer = _ => Task.FromResult(Panel.Sanctions(access: Panel.Access("v1")));
        await loop.Tick(Start + SanctionSyncLoop.Interval);

        Assert.False(access.IsDenied("2"));
    }

    // Bans are enforced from the moment the answer clears a joining player's
    // wait, and a denial has to be known by then too.
    [Fact]
    public async Task AJoiningPlayersDenialIsKnownWhenTheirWaitEnds()
    {
        var (loop, book, access, panel, _) = WithAccess();
        book.Joined("1");
        panel.Answer = _ => Task.FromResult(Panel.Sanctions(access: Panel.Access("v1", "1")));

        await loop.Tick(Start);

        Assert.False(book.IsAwaiting("1"));
        Assert.True(access.IsDenied("1"));
    }

    [Fact]
    public async Task AFailedFetchKeepsTheLastListAndRetriesOnTheNextSync()
    {
        var (loop, _, access, panel, warnings) = WithAccess();
        panel.Answer = _ => Task.FromResult(Panel.Sanctions(access: Panel.Access("v1")));
        panel.AccessAnswer = () => Panel.AccessList("v1", "1");
        await loop.Tick(Start);

        panel.Answer = _ => Task.FromResult(Panel.Sanctions(access: Panel.Access("v2")));
        panel.AccessAnswer = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        await loop.Tick(Start + SanctionSyncLoop.Interval);
        await loop.Tick(Start + SanctionSyncLoop.Interval * 2);

        Assert.Equal(3, panel.AccessFetches);
        Assert.Equal(eServerAccess.Allowed, access.Decide("1"));
        Assert.Equal("v1", access.Snapshot().Version);
        Assert.Equal("503", access.Snapshot().Error);
        Assert.Single(warnings);
        Assert.Null(loop.Status().LastError);
    }

    [Fact]
    public async Task AnOpenServerNeedsNoFetch()
    {
        var (loop, _, access, panel, _) = WithAccess();
        panel.Answer = _ => Task.FromResult(Panel.Sanctions(access: Panel.Access("open")));

        await loop.Tick(Start);

        Assert.Equal(0, panel.AccessFetches);
        Assert.Equal(eServerAccess.Open, access.Decide("1"));
    }

    [Fact]
    public async Task APanelWithoutAccessListsChangesNothing()
    {
        var (loop, _, access, panel, _) = WithAccess();
        access.Load(true, "v1", ["1"]);

        await loop.Tick(Start);

        Assert.Equal(0, panel.AccessFetches);
        Assert.Equal(eServerAccess.Allowed, access.Decide("1"));
        Assert.Equal(eServerAccess.Denied, access.Decide("2"));
    }
}
