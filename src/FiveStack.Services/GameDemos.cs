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
        int demoCount = Directory.Exists(demoPath) ? Directory.GetFiles(demoPath, "*").Length : 0;

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

    public async Task UploadDemos()
    {
        string demoPath = GetMatchDemoPath();
        _logger.LogInformation($"Uploading demos from {demoPath}");

        if (!Directory.Exists(demoPath))
        {
            _logger.LogCritical($"Demo directory does not exist: {demoPath}");
            return;
        }

        string[] files = Directory.GetFiles(demoPath, "*");

        if (files.Length == 0)
        {
            _logger.LogWarning($"No demo files found in {demoPath}");
            return;
        }

        _logger.LogInformation($"Found {files.Length} demo file(s) in {demoPath}");

        foreach (string file in files)
        {
            long size = new FileInfo(file).Length;
            _logger.LogInformation($"Uploading demo {file} ({size} bytes)");
            await UploadDemo(file);
        }
        _logger.LogInformation("Uploaded all demos");
    }

    // Scans the demos directory for leftover .dem files and attempts to upload
    // them. Demos are normally uploaded by the GameEnd timer chain, but that
    // lives entirely in memory — a server crash/restart during the post-match
    // window drops the pending upload and orphans the file on disk. Running this
    // on startup self-heals those cases. Safe to retry: the API rejects demos
    // for maps that aren't finished (409) and cleans up ones already uploaded
    // (406) or whose map is gone (410).
    public async Task UploadOrphanedDemos()
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

        foreach (string file in files)
        {
            // Skip a demo that belongs to a match that is actively recording right
            // now, so we never upload a partially-written file.
            string? matchId = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(file)));
            if (
                Guid.TryParse(matchId, out Guid parsedMatchId)
                && File.Exists(GetLockFilePath(parsedMatchId))
            )
            {
                _logger.LogInformation(
                    $"Skipping demo for actively recording match {matchId}: {file}"
                );
                continue;
            }

            _logger.LogInformation($"Recovering orphaned demo {file}");
            await UploadDemo(file);
        }

        _logger.LogInformation("Finished recovering orphaned demos");
    }

    public async Task UploadDemo(string filePath)
    {
        try
        {
            if (_environmentService.IsOfflineMode())
            {
                return;
            }

            string? serverId = _environmentService.GetServerId();
            string? apiPassword = _environmentService.GetServerApiPassword();

            if (serverId == null || apiPassword == null)
            {
                return;
            }

            // Demos live at {_rootDir}/demos/{matchId}/{mapId}/{demo}.dem — derive
            // the ids from the path rather than GetCurrentMatch() so uploads work
            // even when the demo belongs to a match that is no longer current
            // (e.g. recovered on startup after a crash/restart).
            string demoName = Path.GetFileName(filePath);
            string? mapId = Path.GetFileName(Path.GetDirectoryName(filePath));
            string? matchId = Path.GetFileName(
                Path.GetDirectoryName(Path.GetDirectoryName(filePath))
            );

            if (!Guid.TryParse(matchId, out _) || !Guid.TryParse(mapId, out _))
            {
                _logger.LogWarning(
                    $"Skipping demo with unexpected path (cannot derive match/map ids): {filePath}"
                );
                return;
            }

            string? presignedUrl = await GetPresignedUrl(matchId, mapId, filePath);
            if (string.IsNullOrEmpty(presignedUrl))
            {
                // GetPresignedUrl already logs the reason (and cleans up the file
                // when the map is already uploaded or gone).
                return;
            }

            using var httpClient = new HttpClient();
            httpClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan;

            var fileInfo = new FileInfo(filePath);
            using (var fileStream = File.OpenRead(filePath))
            {
                var request = new HttpRequestMessage(HttpMethod.Put, presignedUrl);
                request.Content = new StreamContent(fileStream);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue(
                    "application/octet-stream"
                );
                request.Content.Headers.ContentLength = fileInfo.Length;

                _logger.LogInformation(
                    $"PUT demo {demoName} ({fileInfo.Length} bytes) for match {matchId}"
                );

                var response = await httpClient.SendAsync(request);

                _logger.LogInformation(
                    $"demo PUT response {(int)response.StatusCode} {response.StatusCode} (match {matchId} demo {demoName})"
                );

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"demo uploaded (match {matchId} demo {demoName})");

                    var notifyEndpoint =
                        $"{_environmentService.GetDemosUrl()}/demos/{matchId}/uploaded";
                    var notifyRequest = new
                    {
                        demo = demoName,
                        mapId = mapId,
                        size = fileInfo.Length,
                    };

                    using var notifyClient = new HttpClient();
                    notifyClient.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", apiPassword);
                    var notifyResponse = await notifyClient.PostAsJsonAsync(
                        notifyEndpoint,
                        notifyRequest
                    );

                    _logger.LogInformation(
                        $"demo uploaded notify response {(int)notifyResponse.StatusCode} {notifyResponse.StatusCode} (match {matchId} demo {demoName})"
                    );

                    if (notifyResponse.IsSuccessStatusCode)
                    {
                        File.Delete(filePath);
                    }
                    else
                    {
                        _logger.LogCritical(
                            $"Failed to notify about demo upload: {notifyResponse.StatusCode}"
                        );
                    }
                }
                else
                {
                    _logger.LogCritical($"unable to upload demo {response.StatusCode}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical($"An error occurred during file upload: {ex.Message}");
        }
    }

    private async Task<string?> GetPresignedUrl(string matchId, string mapId, string filePath)
    {
        string? apiPassword = _environmentService.GetServerApiPassword();
        if (apiPassword == null)
        {
            return null;
        }

        string demoName = Path.GetFileName(filePath);
        string endpoint = $"{_environmentService.GetDemosUrl()}/demos/{matchId}/pre-signed";

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            apiPassword
        );

        var requestBody = new
        {
            demo = demoName,
            mapId = mapId,
        };

        var response = await httpClient.PostAsJsonAsync(endpoint, requestBody);

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
                break;
            case HttpStatusCode.Gone:
                _logger.LogInformation("match map not found");
                File.Delete(filePath);
                break;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogCritical($"unable to get presigned url: {response.StatusCode}");
            return null;
        }

        var responseContent = await response.Content.ReadFromJsonAsync<PresignedUrlResponse>();

        return responseContent?.PresignedUrl;
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
