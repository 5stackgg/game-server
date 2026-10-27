using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FiveStack.Entities;
using FiveStack.Enums;

namespace FiveStack.Simulator;

// MatchStatus is the match's own status, which the plugin has no use for:
// players only start joining once the api has called the match Live.
public sealed record MatchFetch(
    eCurrentMatchAction Action,
    MatchData? Match = null,
    string? MatchStatus = null
);

public interface IMatchSource
{
    Task<MatchFetch> Fetch(CancellationToken token);
}

public interface IDemoUploader
{
    Task Upload(Guid matchId, Guid mapId, CancellationToken token);
}

public sealed class ApiClient : IMatchSource, IDemoUploader
{
    private readonly SimulatorSettings _settings;
    private readonly HttpClient _http;

    public ApiClient(SimulatorSettings settings)
    {
        _settings = settings;

        _http = new HttpClient(
            Network.Handler(
                Uri.TryCreate(settings.ApiUrl, UriKind.Absolute, out Uri? api) ? api : null
            )
        )
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    public async Task<MatchFetch> Fetch(CancellationToken token)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_settings.ApiUrl}/matches/current-match/{_settings.ServerId}"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _settings.ApiPassword
        );

        using HttpResponseMessage response = await _http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();

        return Read(await response.Content.ReadAsStringAsync(token));
    }

    // GameDemos.UploadDemos: a presigned url from the api, the file PUT to it,
    // then the api told it is there. There is no game to record, so a small
    // stand-in goes up where the .dem would.
    public async Task Upload(Guid matchId, Guid mapId, CancellationToken token)
    {
        string demo = $"{matchId}_{mapId}.dem";
        byte[] file = StandInDemo();

        using HttpResponseMessage presign = await PostDemos(
            $"{matchId}/pre-signed",
            new { demo, mapId },
            token
        );
        if (presign.StatusCode == System.Net.HttpStatusCode.NotAcceptable)
        {
            Log.Info("demo is already uploaded");
            return;
        }
        presign.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(
            await presign.Content.ReadAsStringAsync(token)
        );
        string url = body.RootElement.GetProperty("presignedUrl").GetString() ?? "";

        using var put = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new ByteArrayContent(file),
        };
        put.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using HttpResponseMessage stored = await _http.SendAsync(put, token);
        stored.EnsureSuccessStatusCode();

        using HttpResponseMessage uploaded = await PostDemos(
            $"{matchId}/uploaded",
            new
            {
                demo,
                mapId,
                size = file.Length,
            },
            token
        );
        uploaded.EnsureSuccessStatusCode();
        Log.Info($"demo {demo} uploaded ({file.Length} bytes)");
    }

    public static byte[] StandInDemo()
    {
        byte[] file = new byte[1024];
        "HL2DEMO\0"u8.CopyTo(file);
        return file;
    }

    private async Task<HttpResponseMessage> PostDemos(
        string path,
        object body,
        CancellationToken token
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_settings.DemosUrl}/demos/{path}"
        )
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _settings.ApiPassword
        );

        return await _http.SendAsync(request, token);
    }

    // CurrentMatchUtility.Read, without the plugin's logger.
    public static MatchFetch Read(string body)
    {
        if (body.Length == 0)
        {
            return new MatchFetch(eCurrentMatchAction.Clear);
        }

        try
        {
            MatchData? match = JsonSerializer.Deserialize<MatchData>(body);

            if (match == null)
            {
                return new MatchFetch(eCurrentMatchAction.Ignore);
            }

            using JsonDocument document = JsonDocument.Parse(body);
            string? status =
                document.RootElement.TryGetProperty("status", out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;

            return new MatchFetch(eCurrentMatchAction.Setup, match, status);
        }
        catch (JsonException error)
        {
            Log.Warn($"current match is not valid JSON: {error.Message}");
            return new MatchFetch(eCurrentMatchAction.Ignore);
        }
    }

    public async Task Ping(string map, int players, CancellationToken token)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_settings.ApiUrl}{PingPath(_settings.ServerId!, map, _settings.PluginVersion, players)}"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _settings.ApiPassword
        );

        using HttpResponseMessage response = await _http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
    }

    // The runtime is left for the api to ignore: it only knows the two real
    // ones, and the simulator is neither. Never on the Steam relay.
    public static string PingPath(string serverId, string map, string pluginVersion, int players)
    {
        string escaped = Uri.EscapeDataString(map);

        return $"/game-server-node/ping/{serverId}?map={escaped}&pluginVersion={Uri.EscapeDataString(pluginVersion)}&pluginRuntime=simulator&hibernating=false&players={players}&mapName={escaped}";
    }
}
