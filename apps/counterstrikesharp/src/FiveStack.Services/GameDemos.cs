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

    public async Task UploadDemo(string filePath)
    {
        try
        {
            if (_environmentService.IsOfflineMode())
            {
                return;
            }

            MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

            string? serverId = _environmentService.GetServerId();
            string? apiPassword = _environmentService.GetServerApiPassword();

            if (serverId == null || apiPassword == null || match == null)
            {
                return;
            }

            string demoName = Path.GetFileName(filePath);

            string? presignedUrl = await GetPresignedUrl(filePath);
            if (string.IsNullOrEmpty(presignedUrl))
            {
                _logger.LogCritical(
                    $"Failed to get presigned URL (match {match.id} map {match.current_match_map_id} demo {demoName})"
                );
                return;
            }

            // dedicated client: the S3 PUT needs an unbounded timeout
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
                    $"PUT demo {demoName} ({fileInfo.Length} bytes) for match {match.id}"
                );

                var response = await httpClient.SendAsync(request);

                _logger.LogInformation(
                    $"demo PUT response {(int)response.StatusCode} {response.StatusCode} (match {match.id} demo {demoName})"
                );

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"demo uploaded (match {match.id} demo {demoName})");

                    var notifyEndpoint =
                        $"{_environmentService.GetDemosUrl()}/demos/{match.id}/uploaded";
                    var notifyRequest = new
                    {
                        demo = demoName,
                        mapId = match.current_match_map_id,
                        size = fileInfo.Length,
                    };

                    var notifyHttpRequest = new HttpRequestMessage(HttpMethod.Post, notifyEndpoint)
                    {
                        Content = JsonContent.Create(notifyRequest),
                    };
                    notifyHttpRequest.Headers.Authorization = new AuthenticationHeaderValue(
                        "Bearer",
                        apiPassword
                    );
                    var notifyResponse = await HttpClientProvider.Client.SendAsync(
                        notifyHttpRequest
                    );

                    _logger.LogInformation(
                        $"demo uploaded notify response {(int)notifyResponse.StatusCode} {notifyResponse.StatusCode} (match {match.id} demo {demoName})"
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

    private async Task<string?> GetPresignedUrl(string filePath)
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match == null)
        {
            return null;
        }

        string? apiPassword = _environmentService.GetServerApiPassword();
        if (apiPassword == null)
        {
            return null;
        }

        string endpoint = $"{_environmentService.GetDemosUrl()}/demos/{match.id}/pre-signed";

        var requestBody = new
        {
            demo = Path.GetFileName(filePath),
            mapId = _matchService.GetCurrentMatch()?.GetMatchData()?.current_match_map_id,
        };

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(requestBody),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiPassword);

        var response = await HttpClientProvider.Client.SendAsync(request);

        _logger.LogInformation(
            $"presigned url response {(int)response.StatusCode} {response.StatusCode} (match {match.id} map {match.current_match_map_id} demo {Path.GetFileName(filePath)})"
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
