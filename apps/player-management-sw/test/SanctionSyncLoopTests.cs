using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FiveStack.Entities.PlayerManagement;
using FiveStack.Utilities;
using Xunit;

public class SanctionSyncLoopTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private sealed class Panel : HttpMessageHandler
    {
        public readonly List<List<string>> Asked = new();
        public Func<List<string>, Task<HttpResponseMessage>> Answer = _ =>
            Task.FromResult(Sanctions());

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
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

        public static HttpResponseMessage Sanctions(string json = "[]")
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"{{\"sanctions\":{json}}}",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        }
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
        Panel panel = new();
        SanctionBook book = new();
        List<string> warnings = new();

        SanctionSyncLoop loop = new(
            book,
            new SanctionsClient(new HttpClient(panel)),
            settings ?? Connected,
            "0.0.9",
            "swiftlys2",
            warnings.Add,
            _ => { }
        );

        return (loop, book, panel, warnings);
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
        loop.Observe(["1", "2", "1"]);
        book.Joined("3");

        await loop.Tick(Start);

        Assert.Equal(["1", "2", "3"], panel.Asked[0]);
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
}
