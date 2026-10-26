using System.Net.Http.Headers;
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

public sealed class ApiClient : IMatchSource
{
    private readonly SimulatorSettings _settings;
    private readonly HttpClient _http;

    public ApiClient(SimulatorSettings settings)
    {
        _settings = settings;

        var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        if (Uri.TryCreate(settings.ApiUrl, UriKind.Absolute, out Uri? api))
        {
            handler.SslOptions.RemoteCertificateValidationCallback = Tls.For(api);
        }

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
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
