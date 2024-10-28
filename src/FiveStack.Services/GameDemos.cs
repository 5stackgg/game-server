using System.Net.Http.Headers;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class GameDemos
{
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly EnvironmentService _environmentService;
    private readonly ILogger<GameDemos> _logger;
    private string _rootDir = "/opt";

    public GameDemos(
        ILogger<GameDemos> logger,
        GameServer gameServer,
        MatchService matchService,
        EnvironmentService environmentService
    )
    {
        _logger = logger;
        _gameServer = gameServer;
        _matchService = matchService;
        _environmentService = environmentService;

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
            return;
        }

        File.Create(lockFilePath).Dispose();

        _gameServer.Message(HudDestination.Alert, "Recording Demo");

        Directory.CreateDirectory(GetMatchDemoPath());

        _gameServer.SendCommands(
            new[]
            {
                $"tv_delay {match.options.tv_delay}",
                $"tv_record {GetMatchDemoPath()}/{MatchUtility.GetSafeMatchPrefix(match)}_{DateTime.Now.ToString("yyyyMMdd-HHmm")}-{Server.MapName}",
            }
        );
    }

    public void Stop()
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match == null)
        {
            return;
        }

        File.Delete(GetLockFilePath(match.id));
        _gameServer.SendCommands(new[] { "tv_stoprecord" });
    }

    public async Task UploadDemos()
    {
        _logger.LogInformation($"Uploading demos from {GetMatchDemoPath()}");
        string[] files = Directory.GetFiles(GetMatchDemoPath(), "*");

        foreach (string file in files)
        {
            _logger.LogInformation($"uploading demo {file}");
            await UploadDemo(file);
        }
        _logger.LogInformation("Uploaded demos");
    }

    public async Task UploadDemo(string filePath)
    {
        try
        {
            MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

            string? serverId = _environmentService.GetServerId();
            string? apiPassword = _environmentService.GetServerApiPassword();

            if (serverId == null || apiPassword == null || match == null)
            {
                return;
            }

            string endpoint =
                $"{_environmentService.GetDemosUrl()}/demos/{match.id}/map/{match.current_match_map_id}";

            _logger.LogInformation($"Uploading Demo {endpoint}");

            using var httpClient = new HttpClient();
            httpClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan;

            using (var fileStream = File.OpenRead(filePath))
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    apiPassword
                );

                using (var formData = new MultipartFormDataContent())
                using (var streamContent = new StreamContent(fileStream))
                {
                    streamContent.Headers.ContentType = new MediaTypeHeaderValue(
                        "application/octet-stream"
                    );
                    formData.Add(streamContent, "file", Path.GetFileName(filePath));

                    var response = await httpClient.PostAsync(endpoint, formData);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("demo uploaded");
                        File.Delete(filePath);
                    }
                    else
                    {
                        _logger.LogError($"unable to upload demo {response.StatusCode}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"An error occurred during file upload: {ex.Message}");
        }
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
