using System.Net.Http.Headers;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class MatchDemos
{
    private readonly GameServer _gameServer;
    private readonly EnvironmentService _environmentService;
    private readonly ILogger<MatchDemos> _logger;

    public MatchDemos(
        ILogger<MatchDemos> logger,
        GameServer gameServer,
        EnvironmentService environmentService
    )
    {
        _logger = logger;
        _gameServer = gameServer;
        _environmentService = environmentService;
    }

    public void Start(FiveStackMatch match)
    {
        string lockFilePath = GetLockFilePath();
        if (File.Exists(lockFilePath))
        {
            return;
        }

        File.Create(lockFilePath).Dispose();

        _gameServer.Message(HudDestination.Alert, "Recording Demo");

        Directory.CreateDirectory(GetMatchDemoPath(match));

        _gameServer.SendCommands(
            new[]
            {
                $"tv_record /opt/demos/{GetMatchDemoPath(match)}/{MatchUtility.GetSafeMatchPrefix(match)}_{DateTime.Now.ToString("yyyyMMdd-HHmm")}-{Server.MapName}"
            }
        );
    }

    public void StopDemoRecording()
    {
        File.Delete(GetLockFilePath());
        _gameServer.SendCommands(new[] { "tv_stoprecord" });
    }

    public async Task UploadDemos(FiveStackMatch match)
    {
        string[] files = Directory.GetFiles(GetMatchDemoPath(match), "*");

        foreach (string file in files)
        {
            await UploadDemo(match, file);
        }
    }

    public async Task UploadDemo(FiveStackMatch match, string filePath)
    {
        string? serverId = _environmentService.GetServerId();
        string? apiPassword = _environmentService.GetServerApiPassword();

        if (serverId == null || apiPassword == null)
        {
            return;
        }

        string endpoint =
            $"https://api.5stack.gg/server/{serverId}/match/{match.id}/{match.current_match_map_id}/demo";

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

    private string GetMatchDemoPath(FiveStackMatch match)
    {
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
