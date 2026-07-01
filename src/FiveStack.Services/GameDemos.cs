using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public record PresignedUrlResponse
{
    [JsonPropertyName("presignedUrl")]
    public string? PresignedUrl { get; init; }
}

public class GameDemos
{
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly EnvironmentService _environmentService;
    private readonly ILogger<GameDemos> _logger;
    private readonly IStringLocalizer _localizer;
    private string _rootDir = "/opt";

    // A finalized demo hasn't been written since tv_stoprecord; a file touched
    // more recently than this may still be recording, so recovery skips it.
    private const int RecordingFinalizeWindowSeconds = 60;

    // Shared to avoid socket churn; per-request auth is set on each message.
    // Timeout is infinite by design — the caller's CancellationToken bounds it.
    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan,
    };

    // Guards against two paths (GameEnd, orphan recovery, the console command)
    // uploading/deleting the same demo at once.
    private readonly HashSet<string> _uploadsInProgress = new();
    private readonly object _uploadsLock = new();

    public GameDemos(
        ILogger<GameDemos> logger,
        GameServer gameServer,
        MatchService matchService,
        EnvironmentService environmentService,
        IStringLocalizer localizer
    )
    {
        _logger = logger;
        _gameServer = gameServer;
        _matchService = matchService;
        _environmentService = environmentService;
        _localizer = localizer;

        if (
            !Directory.Exists(_rootDir)
            || new DirectoryInfo(_rootDir).Attributes.HasFlag(FileAttributes.ReadOnly)
        )
        {
            _rootDir = Server.GameDirectory;
        }
    }

    public void Start()
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match == null)
        {
            return;
        }

        string lockFilePath = GetLockFilePath(match.id);
        if (File.Exists(lockFilePath))
        {
            _logger.LogInformation("Demo is already recording");
            return;
        }

        File.Create(lockFilePath).Dispose();

        _gameServer.Message(HudDestination.Alert, _localizer["demos.recording"]);

        Directory.CreateDirectory(GetMatchDemoPath());

        string demoPath =
            $"{GetMatchDemoPath()}/{MatchUtility.GetSafeMatchPrefix(match)}_{DateTime.Now.ToString("yyyyMMdd-HHmm")}-{Server.MapName}";
        _logger.LogInformation($"Recording demo to {demoPath}");

        _gameServer.SendCommands([$"tv_record {demoPath}"]);
    }

    public void Stop()
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match == null)
        {
            return;
        }

        string demoPath = GetMatchDemoPath();
        int demoCount = Directory.Exists(demoPath)
            ? Directory.GetFiles(demoPath, "*.dem").Length
            : 0;

        File.Delete(GetLockFilePath(match.id));
        Server.NextFrame(() =>
        {
            _logger.LogInformation(
                $"Stopping demo recording (match {match.id}): {demoCount} file(s) in {demoPath}"
            );
            _gameServer.SendCommands(["tv_stoprecord"]);
        });
    }

    public void StopTV()
    {
        _logger.LogInformation("Stopping TV broadcast");
        _gameServer.SendCommands(["tv_broadcast 0"]);
        Server.NextFrame(() =>
        {
            _gameServer.SendCommands(["tv_stop"]);
        });
    }

    // Uploads the current match/map's demos.
    public Task<bool> UploadDemos(System.Threading.CancellationToken cancellationToken = default)
    {
        return UploadDemosFromPath(GetMatchDemoPath(), cancellationToken);
    }

    // Uploads a specific match/map's demos, independent of the current match
    // state (which can change during the post-match upload window).
    public Task<bool> UploadDemos(
        string matchId,
        string mapId,
        System.Threading.CancellationToken cancellationToken = default
    )
    {
        return UploadDemosFromPath($"{_rootDir}/demos/{matchId}/{mapId}", cancellationToken);
    }

    // Returns false if any demo still needs another attempt.
    private async Task<bool> UploadDemosFromPath(
        string demoPath,
        System.Threading.CancellationToken cancellationToken
    )
    {
        _logger.LogInformation($"Uploading demos from {demoPath}");

        if (!Directory.Exists(demoPath))
        {
            _logger.LogCritical($"Demo directory does not exist: {demoPath}");
            return true;
        }

        string[] files = Directory.GetFiles(demoPath, "*.dem");

        if (files.Length == 0)
        {
            _logger.LogWarning($"No demo files found in {demoPath}");
            return true;
        }

        _logger.LogInformation($"Found {files.Length} demo file(s) in {demoPath}");

        bool allUploaded = true;
        foreach (string file in files)
        {
            _logger.LogInformation($"Uploading demo {file}");
            if (!await UploadDemo(file, cancellationToken))
            {
                allUploaded = false;
            }
        }

        if (allUploaded)
        {
            _logger.LogInformation("Uploaded all demos");
        }

        return allUploaded;
    }

    // Recording locks never survive a process restart, so any left on disk at
    // startup are stale from a crash. Clear them so Start() doesn't refuse to
    // record a resumed match.
    public void ClearStaleRecordingLocks()
    {
        try
        {
            foreach (string lockFile in Directory.GetFiles(_rootDir, ".recording-demo-*"))
            {
                File.Delete(lockFile);
                _logger.LogInformation($"Cleared stale recording lock {lockFile}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to clear stale recording locks: {ex.Message}");
        }
    }

    // Uploads .dem files left on disk by a crash/restart during the post-match
    // upload window. Runs on startup to self-heal.
    public async Task UploadOrphanedDemos(
        System.Threading.CancellationToken cancellationToken = default
    )
    {
        if (_environmentService.IsOfflineMode())
        {
            return;
        }

        string demosRoot = $"{_rootDir}/demos";
        if (!Directory.Exists(demosRoot))
        {
            return;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(demosRoot, "*.dem", SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to scan for orphaned demos: {ex.Message}");
            return;
        }

        if (files.Length == 0)
        {
            return;
        }

        _logger.LogInformation(
            $"Found {files.Length} demo(s) on disk, attempting to recover uploads"
        );

        // One overall budget for the whole pass so a backlog can't run for
        // N × timeLimit; whatever isn't reached is retried on the next start.
        using var cts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        cts.CancelAfter(TimeSpan.FromSeconds(_environmentService.GetDemoUploadTimeLimitSeconds()));

        foreach (string file in files)
        {
            cts.Token.ThrowIfCancellationRequested();

            // Skip anything still being recorded: an active recording holds a lock
            // for its match, and a finalized demo hasn't been written since
            // tv_stoprecord. The lock check covers the case where an active
            // recording simply hasn't flushed within the freshness window.
            string? matchId = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(file)));
            bool activelyRecording =
                Guid.TryParse(matchId, out Guid parsedMatchId)
                && File.Exists(GetLockFilePath(parsedMatchId));
            bool recentlyWritten =
                File.GetLastWriteTimeUtc(file)
                > DateTime.UtcNow.AddSeconds(-RecordingFinalizeWindowSeconds);

            if (activelyRecording || recentlyWritten)
            {
                _logger.LogInformation($"Skipping demo that may still be recording: {file}");
                continue;
            }

            _logger.LogInformation($"Recovering orphaned demo {file}");
            await UploadDemo(file, cts.Token);
        }

        _logger.LogInformation("Finished recovering orphaned demos");
    }

    // Returns true when resolved (uploaded, or already-uploaded/map-gone);
    // false on a retryable failure.
    public async Task<bool> UploadDemo(
        string filePath,
        System.Threading.CancellationToken cancellationToken = default
    )
    {
        if (_environmentService.IsOfflineMode())
        {
            return true;
        }

        string? apiPassword = _environmentService.GetServerApiPassword();

        if (apiPassword == null)
        {
            _logger.LogWarning("Cannot upload demo: server api password not configured");
            return false;
        }

        // Derive ids from the path ({root}/demos/{matchId}/{mapId}/{demo}.dem)
        // so this works for demos that aren't the current match.
        string demoName = Path.GetFileName(filePath);
        string? mapId = Path.GetFileName(Path.GetDirectoryName(filePath));
        string? matchId = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(filePath)));

        if (!Guid.TryParse(matchId, out _) || !Guid.TryParse(mapId, out _))
        {
            _logger.LogWarning(
                $"Skipping demo with unexpected path (cannot derive match/map ids): {filePath}"
            );
            return true;
        }

        lock (_uploadsLock)
        {
            if (!_uploadsInProgress.Add(filePath))
            {
                // Another path owns this upload; report not-done so the caller
                // retries rather than treating it as a confirmed success.
                _logger.LogInformation($"Demo already being uploaded, skipping: {demoName}");
                return false;
            }
        }

        try
        {
            var (presignedUrl, resolved) = await GetPresignedUrl(
                matchId,
                mapId,
                filePath,
                apiPassword,
                cancellationToken
            );

            if (resolved)
            {
                return true;
            }

            if (string.IsNullOrEmpty(presignedUrl))
            {
                // GetPresignedUrl already logged the specific reason (e.g. 409
                // "not finished yet", which is a normal retry, not an error).
                return false;
            }

            var fileInfo = new FileInfo(filePath);
            using var fileStream = File.OpenRead(filePath);

            var request = new HttpRequestMessage(HttpMethod.Put, presignedUrl);
            request.Content = new StreamContent(fileStream);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(
                "application/octet-stream"
            );
            request.Content.Headers.ContentLength = fileInfo.Length;

            _logger.LogInformation(
                $"PUT demo {demoName} ({fileInfo.Length} bytes) for match {matchId}"
            );

            var response = await _httpClient.SendAsync(request, cancellationToken);

            _logger.LogInformation(
                $"demo PUT response {(int)response.StatusCode} {response.StatusCode} (match {matchId} demo {demoName})"
            );

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogCritical($"unable to upload demo {response.StatusCode}");
                return false;
            }

            _logger.LogInformation($"demo uploaded (match {matchId} demo {demoName})");

            var notifyRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_environmentService.GetDemosUrl()}/demos/{matchId}/uploaded"
            )
            {
                Content = JsonContent.Create(
                    new
                    {
                        demo = demoName,
                        mapId = mapId,
                        size = fileInfo.Length,
                    }
                ),
            };
            notifyRequest.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                apiPassword
            );

            var notifyResponse = await _httpClient.SendAsync(notifyRequest, cancellationToken);

            _logger.LogInformation(
                $"demo uploaded notify response {(int)notifyResponse.StatusCode} {notifyResponse.StatusCode} (match {matchId} demo {demoName})"
            );

            if (!notifyResponse.IsSuccessStatusCode)
            {
                _logger.LogCritical(
                    $"Failed to notify about demo upload: {notifyResponse.StatusCode}"
                );
                return false;
            }

            File.Delete(filePath);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning($"Demo upload canceled (time budget reached): {filePath}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogCritical($"An error occurred during file upload: {ex.Message}");
            return false;
        }
        finally
        {
            lock (_uploadsLock)
            {
                _uploadsInProgress.Remove(filePath);
            }
        }
    }

    // "resolved" = nothing left to upload (already uploaded / map gone); file cleaned up.
    private async Task<(string? PresignedUrl, bool Resolved)> GetPresignedUrl(
        string matchId,
        string mapId,
        string filePath,
        string apiPassword,
        System.Threading.CancellationToken cancellationToken = default
    )
    {
        string demoName = Path.GetFileName(filePath);
        string endpoint = $"{_environmentService.GetDemosUrl()}/demos/{matchId}/pre-signed";

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new { demo = demoName, mapId = mapId }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiPassword);

        var response = await _httpClient.SendAsync(request, cancellationToken);

        _logger.LogInformation(
            $"presigned url response {(int)response.StatusCode} {response.StatusCode} (match {matchId} map {mapId} demo {demoName})"
        );

        switch (response.StatusCode)
        {
            case HttpStatusCode.Conflict:
                _logger.LogInformation("match map is not finished");
                break;
            case HttpStatusCode.NotAcceptable:
                _logger.LogInformation("demo is already uploaded");
                File.Delete(filePath);
                return (null, true);
            case HttpStatusCode.Gone:
                _logger.LogInformation("match map not found");
                File.Delete(filePath);
                return (null, true);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogCritical($"unable to get presigned url: {response.StatusCode}");
            return (null, false);
        }

        var responseContent = await response.Content.ReadFromJsonAsync<PresignedUrlResponse>(
            cancellationToken
        );

        if (string.IsNullOrEmpty(responseContent?.PresignedUrl))
        {
            _logger.LogCritical(
                $"presigned url response had no url (match {matchId} map {mapId} demo {demoName})"
            );
            return (null, false);
        }

        return (responseContent.PresignedUrl, false);
    }

    private string GetMatchDemoPath()
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();
        if (match == null || match.current_match_map_id == null)
        {
            return $"{_rootDir}/demos";
        }

        return $"{_rootDir}/demos/{match.id}/{match.current_match_map_id}";
    }

    private string GetLockFilePath(Guid matchId)
    {
        return $"{_rootDir}/.recording-demo-{matchId}";
    }
}
