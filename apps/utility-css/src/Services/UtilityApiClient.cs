using System.Net.Http;
using System.Text;
using System.Text.Json;
using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace UtilityPractice;

// Everything the plugin says to the panel goes through here. Two rules hold for
// every method: it never throws at its caller, and it never runs on the game
// thread past its first await, because a practice server that stutters while a
// lineup uploads is worse than one that loses the upload.
//
// This is also the only place that knows the API's shapes. The API owns the
// wire contract, so LineupRecord is translated to and from it here rather than
// being sent as-is.
public class UtilityApiClient
{
    // A save that could not reach the panel is worth keeping, but only so many:
    // an offline practice server left running overnight must not grow forever.
    private const int MaxQueued = 64;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly UtilityConfig _config;
    private readonly ILogger<UtilityApiClient> _logger;

    private readonly object _queueLock = new object();
    private readonly Queue<LineupRecord> _retryQueue = new Queue<LineupRecord>();
    private readonly Queue<UtilityPracticeResultPayload> _resultQueue =
        new Queue<UtilityPracticeResultPayload>();
    private readonly SemaphoreSlim _draining = new SemaphoreSlim(1, 1);

    public UtilityApiClient(UtilityConfig config, ILogger<UtilityApiClient> logger)
    {
        _config = config;
        _logger = logger;
    }

    private class IngestResponse
    {
        public string? id { get; set; }
    }

    public int QueuedCount
    {
        get
        {
            lock (_queueLock)
            {
                return _retryQueue.Count + _resultQueue.Count;
            }
        }
    }

    public async Task<string?> Ingest(LineupRecord record)
    {
        string? id = await Post(record);

        if (id == null)
        {
            Enqueue(record);
            return null;
        }

        // The panel is reachable again, so anything held back can go now.
        _ = Drain();

        return id;
    }

    public async Task<List<LineupRecord>?> Library(string map, ulong steamId)
    {
        string? body = await SendText(
            HttpMethod.Get,
            $"/utility/library?map={Uri.EscapeDataString(map)}&steam_id={steamId}",
            null
        );

        if (body == null)
        {
            return null;
        }

        try
        {
            List<UtilityLibraryRow>? rows = ReadList<UtilityLibraryRow>(body, "lineups", "utility");

            return rows == null
                ? new List<LineupRecord>()
                : rows.Select(row => row.ToLineup()).ToList();
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to read the lineup library");
            return null;
        }
    }

    // A library row carries no flight path and no measured bloom, so both cost
    // one more call.
    public async Task<UtilityTrajectoryArtifact?> Trajectory(string id)
    {
        byte[]? body = await Send(
            HttpMethod.Get,
            $"/utility/{Uri.EscapeDataString(id)}/trajectory",
            null
        );

        if (body == null)
        {
            return null;
        }

        try
        {
            return UtilityTrajectoryArtifact.Parse(body);
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to read the trajectory for {id}", id);
            return null;
        }
    }

    // The only way the panel learns anybody is on this server. A match server
    // reports connects over the match-events socket; a practice server has no
    // such socket, and without this every session reads as empty and gets
    // reaped out from under whoever is throwing.
    public async Task Occupancy(IReadOnlyCollection<ulong> steamIds)
    {
        string body = JsonSerializer.Serialize(
            new { steam_ids = steamIds.Select(id => id.ToString()).ToArray() },
            PracticeJson.Options
        );

        await SendText(HttpMethod.Post, "/utility/occupancy", body);
    }

    public async Task<PracticeSessionData?> Session()
    {
        string? body = await SendText(HttpMethod.Get, "/utility/session", null);

        if (body == null)
        {
            return null;
        }

        try
        {
            return JsonSerializer
                .Deserialize<UtilitySessionRow>(body, PracticeJson.Options)
                ?.ToSession();
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to read the practice session");
            return null;
        }
    }

    // The panel recomputes the distance from the lineup it owns, so this is a
    // report and not a claim. A result that could not be delivered goes on the
    // same retry queue a save does -- nobody is waiting to be told about it by
    // then, which is why only the live attempt answers.
    public async Task<UtilityPracticeResult?> PracticeResult(UtilityPracticeResultPayload payload)
    {
        UtilityPracticeResult? result = await PostResult(payload);

        if (result == null)
        {
            EnqueueResult(payload);
            return null;
        }

        _ = Drain();

        return result;
    }

    public async Task<bool> Delete(string id)
    {
        return await SendText(HttpMethod.Delete, $"/utility/{Uri.EscapeDataString(id)}", null)
            != null;
    }

    // Retries oldest first: a player's saves replay in the order they threw
    // them, so the library reads the way the session went.
    public async Task Drain()
    {
        if (!_config.IsConnected() || !await _draining.WaitAsync(0))
        {
            return;
        }

        try
        {
            while (true)
            {
                LineupRecord? record;
                lock (_queueLock)
                {
                    if (!_retryQueue.TryPeek(out record))
                    {
                        break;
                    }
                }

                if (await Post(record) == null)
                {
                    return;
                }

                lock (_queueLock)
                {
                    _retryQueue.TryDequeue(out _);
                }
            }

            while (true)
            {
                UtilityPracticeResultPayload? result;
                lock (_queueLock)
                {
                    if (!_resultQueue.TryPeek(out result))
                    {
                        return;
                    }
                }

                if (await PostResult(result) == null)
                {
                    return;
                }

                lock (_queueLock)
                {
                    _resultQueue.TryDequeue(out _);
                }
            }
        }
        finally
        {
            _draining.Release();
        }
    }

    private void Enqueue(LineupRecord record)
    {
        lock (_queueLock)
        {
            while (_retryQueue.Count >= MaxQueued)
            {
                _retryQueue.TryDequeue(out _);
            }

            _retryQueue.Enqueue(record);
        }
    }

    private void EnqueueResult(UtilityPracticeResultPayload payload)
    {
        lock (_queueLock)
        {
            while (_resultQueue.Count >= MaxQueued)
            {
                _resultQueue.TryDequeue(out _);
            }

            _resultQueue.Enqueue(payload);
        }
    }

    private async Task<string?> Post(LineupRecord record)
    {
        string? body;

        try
        {
            body = JsonSerializer.Serialize(
                UtilityIngestPayload.From(record),
                PracticeJson.Options
            );
        }
        catch (Exception error)
        {
            // Unserializable means it will never succeed; dropping it beats
            // wedging the queue behind it.
            _logger.LogError(error, "unable to serialize lineup {client_id}", record.client_id);
            return null;
        }

        string? response = await SendText(HttpMethod.Post, "/utility/ingest", body);

        if (response == null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<IngestResponse>(response, PracticeJson.Options)?.id;
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to read the ingest response");
            return null;
        }
    }

    private async Task<UtilityPracticeResult?> PostResult(UtilityPracticeResultPayload payload)
    {
        payload.server_id = string.IsNullOrEmpty(_config.ServerId) ? null : _config.ServerId;

        string body;

        try
        {
            body = JsonSerializer.Serialize(payload, PracticeJson.Options);
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to serialize a practice result");
            return null;
        }

        string? response = await SendText(HttpMethod.Post, "/utility/practice-result", body);

        if (response == null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<UtilityPracticeResult>(response, PracticeJson.Options);
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to read the practice result");
            return null;
        }
    }

    // Accepts either a bare array or an envelope naming one, so a wrapper key
    // on the API side is not a silently empty library.
    private static List<T>? ReadList<T>(string body, params string[] properties)
    {
        using JsonDocument document = JsonDocument.Parse(body);

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return document.RootElement.Deserialize<List<T>>(PracticeJson.Options);
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (string property in properties)
        {
            if (
                document.RootElement.TryGetProperty(property, out JsonElement value)
                && value.ValueKind == JsonValueKind.Array
            )
            {
                return value.Deserialize<List<T>>(PracticeJson.Options);
            }
        }

        return null;
    }

    private async Task<string?> SendText(HttpMethod method, string path, string? body)
    {
        byte[]? response = await Send(method, path, body);

        return response == null ? null : PracticeJson.Text(response);
    }

    private async Task<byte[]?> Send(HttpMethod method, string path, string? body)
    {
        if (!_config.IsConnected())
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(method, Url(path));

            if (!string.IsNullOrEmpty(_config.ServerApiPassword))
            {
                request.Headers.TryAddWithoutValidation(
                    "x-server-api-password",
                    _config.ServerApiPassword
                );
            }

            if (body != null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            using var timeout = new CancellationTokenSource(RequestTimeout);
            using HttpResponseMessage response = await HttpClientProvider.Client.SendAsync(
                request,
                timeout.Token
            );

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "{method} {path} returned {status}",
                    method.Method,
                    path,
                    (int)response.StatusCode
                );
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception error)
        {
            _logger.LogError(error, "{method} {path} failed", method.Method, path);
            return null;
        }
    }

    // Every utility endpoint resolves the session from the server rather than from
    // anything the caller names, and it needs the server id to do it.
    private string Url(string path)
    {
        if (string.IsNullOrEmpty(_config.ServerId))
        {
            return $"{_config.UtilityUrl}{path}";
        }

        string separator = path.Contains('?') ? "&" : "?";

        return $"{_config.UtilityUrl}{path}{separator}server_id={Uri.EscapeDataString(_config.ServerId)}";
    }
}
