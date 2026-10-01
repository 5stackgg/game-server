using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FiveStack.Entities.PlayerManagement;
using FiveStack.Utilities;
using Xunit;

public class SanctionsClientTests
{
    private const string ServerId = "11111111-1111-1111-1111-111111111111";

    private static readonly PlayerManagementSettings Settings = new()
    {
        API_DOMAIN = "https://api.example.com",
        SERVER_ID = ServerId,
        SERVER_API_PASSWORD = "secret",
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public HttpRequestMessage? Request;
        public string? Body;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Request = request;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync();

            return _respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private static PlayerSanctionsRequest Request()
    {
        return new PlayerSanctionsRequest
        {
            steam_ids = ["76561198000000001"],
            plugin_version = "0.0.9",
            plugin_runtime = "swiftlys2",
        };
    }

    [Theory]
    [InlineData("{\"sanctions\":[],\"roster_recorded\":true}", true)]
    [InlineData("{\"sanctions\":[],\"roster_recorded\":false}", false)]
    [InlineData("{\"sanctions\":[]}", null)]
    public async Task ItReadsWhetherThePanelRecordedTheRoster(string answer, bool? recorded)
    {
        StubHandler handler = new(_ => Json(HttpStatusCode.OK, answer));

        SanctionSync result = await new SanctionsClient(new HttpClient(handler)).Sync(
            Settings,
            Request()
        );

        Assert.Equal(recorded, result.RosterRecorded);
    }

    [Fact]
    public async Task ItPostsThePlayersToTheServersRouteWithTheApiPassword()
    {
        StubHandler handler = new(_ => Json(HttpStatusCode.OK, "{\"sanctions\":[]}"));

        await new SanctionsClient(new HttpClient(handler)).Sync(Settings, Request());

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(
            $"https://api.example.com/sanctions/server/{ServerId}",
            handler.Request.RequestUri!.ToString()
        );
        Assert.Equal("Bearer secret", handler.Request.Headers.Authorization!.ToString());

        using JsonDocument body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("76561198000000001", body.RootElement.GetProperty("steam_ids")[0].GetString());
        Assert.Equal("0.0.9", body.RootElement.GetProperty("plugin_version").GetString());
        Assert.Equal("swiftlys2", body.RootElement.GetProperty("plugin_runtime").GetString());
    }

    [Fact]
    public async Task ItReadsThePanelsSanctions()
    {
        StubHandler handler = new(_ =>
            Json(
                HttpStatusCode.OK,
                "{\"sanctions\":[{\"steam_id\":\"76561198000000001\",\"type\":\"gag\",\"reason\":\"spam\",\"expires_at\":\"2026-10-01T00:00:00.000Z\"},{\"steam_id\":\"76561198000000001\",\"type\":\"ban\",\"reason\":null,\"expires_at\":null}]}"
            )
        );

        SanctionSync result = await new SanctionsClient(new HttpClient(handler)).Sync(
            Settings,
            Request()
        );

        Assert.Null(result.Error);
        Assert.Equal(2, result.Sanctions!.Count);
        Assert.Equal("gag", result.Sanctions[0].type);
        Assert.Equal("spam", result.Sanctions[0].reason);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            result.Sanctions[0].expires_at
        );
        Assert.Null(result.Sanctions[1].expires_at);
    }

    [Fact]
    public async Task AnUnauthorizedAnswerNamesTheSettingsToCheck()
    {
        StubHandler handler = new(_ => Json(HttpStatusCode.Unauthorized, ""));

        SanctionSync result = await new SanctionsClient(new HttpClient(handler)).Sync(
            Settings,
            Request()
        );

        Assert.Null(result.Sanctions);
        Assert.Contains("SERVER_API_PASSWORD", result.Error);
    }

    [Fact]
    public async Task AnUnreachablePanelIsAnErrorNotAThrow()
    {
        StubHandler handler = new(_ => throw new HttpRequestException("connection refused"));

        SanctionSync result = await new SanctionsClient(new HttpClient(handler)).Sync(
            Settings,
            Request()
        );

        Assert.Null(result.Sanctions);
        Assert.Equal("connection refused", result.Error);
    }

    [Fact]
    public async Task ItReadsTheAccessOnTheSync()
    {
        StubHandler handler = new(_ =>
            Json(
                HttpStatusCode.OK,
                "{\"sanctions\":[],\"access\":{\"restricted\":true,\"version\":\"abc123\",\"denied\":[\"76561198000000002\"],\"message\":\"Members only\"}}"
            )
        );

        SanctionSync result = await new SanctionsClient(new HttpClient(handler)).Sync(
            Settings,
            Request()
        );

        Assert.Null(result.Error);
        Assert.NotNull(result.Access);
        Assert.True(result.Access!.restricted);
        Assert.Equal("abc123", result.Access.version);
        Assert.Equal(["76561198000000002"], result.Access.denied);
        Assert.Equal("Members only", result.Access.message);
    }

    // A panel that predates access lists says nothing about them, which must
    // not read as a failed sync nor as an access list.
    [Fact]
    public async Task APanelWithoutAccessListsIsNotAnError()
    {
        StubHandler handler = new(_ => Json(HttpStatusCode.OK, "{\"sanctions\":[]}"));

        SanctionSync result = await new SanctionsClient(new HttpClient(handler)).Sync(
            Settings,
            Request()
        );

        Assert.Null(result.Error);
        Assert.Empty(result.Sanctions!);
        Assert.Null(result.Access);
    }

    [Fact]
    public async Task ItGetsTheAccessListWithTheApiPassword()
    {
        StubHandler handler = new(_ =>
            Json(
                HttpStatusCode.OK,
                "{\"restricted\":true,\"version\":\"abc123\",\"steam_ids\":[\"76561198000000001\",\"76561198000000002\"]}"
            )
        );

        ServerAccessFetch result = await new SanctionsClient(new HttpClient(handler)).Access(
            Settings
        );

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal(
            $"https://api.example.com/sanctions/server/{ServerId}/access",
            handler.Request.RequestUri!.ToString()
        );
        Assert.Equal("Bearer secret", handler.Request.Headers.Authorization!.ToString());

        Assert.Null(result.Error);
        Assert.True(result.List!.restricted);
        Assert.Equal("abc123", result.List.version);
        Assert.Equal(["76561198000000001", "76561198000000002"], result.List.steam_ids);
    }

    [Fact]
    public async Task AnUnauthorizedAccessListNamesTheSettingsToCheck()
    {
        StubHandler handler = new(_ => Json(HttpStatusCode.Unauthorized, ""));

        ServerAccessFetch result = await new SanctionsClient(new HttpClient(handler)).Access(
            Settings
        );

        Assert.Null(result.List);
        Assert.Contains("SERVER_API_PASSWORD", result.Error);
    }

    [Theory]
    [InlineData("<html>bad gateway</html>")]
    [InlineData("null")]
    [InlineData("{\"restricted\":true,\"steam_ids\":[]}")]
    public async Task AnUnreadableAccessListIsAnErrorNotAList(string body)
    {
        StubHandler handler = new(_ => Json(HttpStatusCode.OK, body));

        ServerAccessFetch result = await new SanctionsClient(new HttpClient(handler)).Access(
            Settings
        );

        Assert.Null(result.List);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public async Task AnUnreachablePanelIsAnAccessErrorNotAThrow()
    {
        StubHandler handler = new(_ => throw new HttpRequestException("connection refused"));

        ServerAccessFetch result = await new SanctionsClient(new HttpClient(handler)).Access(
            Settings
        );

        Assert.Null(result.List);
        Assert.Equal("connection refused", result.Error);
    }

    [Fact]
    public async Task AnUnconfiguredServerNeverAsksForTheAccessList()
    {
        StubHandler handler = new(_ => Json(HttpStatusCode.OK, "{}"));

        ServerAccessFetch result = await new SanctionsClient(new HttpClient(handler)).Access(
            new PlayerManagementSettings()
        );

        Assert.Null(handler.Request);
        Assert.Null(result.List);
    }

    [Fact]
    public async Task AnUnconfiguredServerNeverCallsThePanel()
    {
        StubHandler handler = new(_ => Json(HttpStatusCode.OK, "{\"sanctions\":[]}"));

        SanctionSync result = await new SanctionsClient(new HttpClient(handler)).Sync(
            new PlayerManagementSettings(),
            Request()
        );

        Assert.Null(handler.Request);
        Assert.Null(result.Sanctions);
    }
}
