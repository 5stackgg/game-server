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
    }

    public void Start()
    {
        FiveStackMatch? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match == null)
        {
            return;
        }

        string lockFilePath = GetLockFilePath();
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
                $"tv_record /opt/demos/{GetMatchDemoPath()}/{MatchUtility.GetSafeMatchPrefix(match)}_{DateTime.Now.ToString("yyyyMMdd-HHmm")}-{Server.MapName}"
            }
        );
    }

    public void StopDemoRecording()
    {
        File.Delete(GetLockFilePath());
        _gameServer.SendCommands(new[] { "tv_stoprecord" });
    }

    public async Task UploadDemos()
    {
        _logger.LogInformation($"Uploading demos from {GetMatchDemoPath()}");
        string[] files = Directory.GetFiles(GetMatchDemoPath(), "*");

        foreach (string file in files)
        {
            await UploadDemo(file);
        }
    }

    public async Task UploadDemo(string filePath)
    {
        FiveStackMatch? match = _matchService.GetCurrentMatch()?.GetMatchData();

        string? serverId = _environmentService.GetServerId();
        string? apiPassword = _environmentService.GetServerApiPassword();

        if (serverId == null || apiPassword == null || match == null)
        {
            return;
        }

        string endpoint =
            $"{_environmentService.GetBaseUri()}/matches/{match.id}/demos/map/{match.current_match_map_id}";

        _logger.LogInformation($"Uploading Demo {endpoint}");

        using (var httpClient = new HttpClient())
        {
            using (var formData = new MultipartFormDataContent())
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    apiPassword
                );

                using (var fileStream = File.OpenRead(filePath))
                using (var streamContent = new StreamContent(fileStream))
                {
                    streamContent.Headers.ContentType = new MediaTypeHeaderValue(
                        "application/octet-stream"
                    );
                    formData.Add(streamContent, "file", Path.GetFileName(filePath));

                    var response = await httpClient.PostAsync(endpoint, formData);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("File uploaded successfully.");
                        File.Delete(filePath);
                    }
                    else
                    {
                        _logger.LogError($"File upload failed. Status code: {response.StatusCode}");
                    }
                }
            }
        }
    }

    private string GetMatchDemoPath()
    {
        FiveStackMatch? match = _matchService.GetCurrentMatch()?.GetMatchData();
        if (match == null || match.current_match_map_id == null)
        {
            return "/opt/demos";
        }

        return $"/opt/demos/{match.id}/{match.current_match_map_id}";
    }

    private string GetLockFilePath()
    {
        return "/opt/.recording-demo";
    }
}
