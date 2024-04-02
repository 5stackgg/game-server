using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.enums;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    public async void StartLive()
    {
        UpdateCurrentRound();

        if (_matchData == null || _matchData == null)
        {
            return;
        }

        if (_matchData.type == "Wingman")
        {
            SendCommands(new[] { "game_type 0; game_mode 2" });
        }
        else
        {
            SendCommands(new[] { "game_type 0; game_mode 1" });
        }

        SetupBackup();

        SendCommands(new[] { "mp_warmup_end", "exec live" });

        StartDemoRecording();

        string directoryPath = Path.Join(Server.GameDirectory + "/csgo/");

        string[] files = Directory.GetFiles(directoryPath, GetSafeMatchPrefix() + "*");

        Regex regex = new Regex(@"(\d+)(?!.*\d)");

        int highestNumber = -1;

        foreach (string file in files)
        {
            Match match = regex.Match(Path.GetFileNameWithoutExtension(file));

            if (match.Success)
            {
                int number;
                if (int.TryParse(match.Value, out number))
                {
                    highestNumber = Math.Max(highestNumber, number);
                }
            }
        }

        if (highestNumber != -1)
        {
            Logger.LogInformation(
                $"Found Backup Round File {highestNumber} and were on {_currentRound}"
            );
        }

        if (highestNumber != -1 && _currentRound > 0 && _currentRound >= highestNumber)
        {
            // we are already live, do not restart the match accidently
            return;
        }

        if (highestNumber > _currentRound)
        {
            RestoreBackupRound(highestNumber.ToString(), true);
            return;
        }

        SendCommands(new[] { "mp_restartgame" });

        PublishMapStatus(eMapStatus.Live);

        await Task.Delay(1000);
        Server.NextFrame(() =>
        {
            Message(HudDestination.Alert, "LIVE LIVE LIVE!");
        });
    }

    private void SetupBackup()
    {
        if (_matchData == null)
        {
            return;
        }

        SendCommands(
            new[]
            {
                $"mp_maxrounds {_matchData.mr * 2}",
                $"mp_overtime_enable {_matchData.overtime}",
                $"mp_backup_round_file {GetSafeMatchPrefix()}",
            }
        );
    }

    private void StartDemoRecording()
    {
        if (_matchData == null || _currentMap == null)
        {
            return;
        }

        string lockFilePath = GetLockFilePath();
        if (File.Exists(lockFilePath))
        {
            return;
        }

        File.Create(lockFilePath).Dispose();

        Message(HudDestination.Alert, "Recording Demo");

        Directory.CreateDirectory(GetMatchDemoPath());

        SendCommands(
            new[]
            {
                $"tv_record /opt/demos/{GetMatchDemoPath()}/{GetSafeMatchPrefix()}_{DateTime.Now.ToString("yyyyMMdd-HHmm")}-{_currentMap.map.name}"
            }
        );
    }

    private string GetMatchDemoPath()
    {
        if (_matchData == null || _matchData.current_match_map_id == null)
        {
            return "/opt/demos";
        }

        return $"/opt/demos/{_matchData.id}/{_matchData.current_match_map_id}";
    }

    private void StopDemoRecording()
    {
        File.Delete(GetLockFilePath());
        SendCommands(new[] { "tv_stoprecord" });
    }

    private string GetLockFilePath()
    {
        return "/opt/.recording-demo";
    }

    private async Task UploadDemos()
    {
        if (_matchData == null)
        {
            return;
        }

        string[] files = Directory.GetFiles(GetMatchDemoPath(), "*");

        foreach (string file in files)
        {
            await UploadDemo(file);
        }
    }

    private async Task UploadDemo(string filePath)
{
    // TODO - should be done differently
    string? serverId = Environment.GetEnvironmentVariable("SERVER_ID");
    string? apiPassword = Environment.GetEnvironmentVariable("SERVER_API_PASSWORD");

    if (_matchData == null || serverId == null || apiPassword == null)
    {
        return;
    }

    string endpoint =
        $"https://api.5stack.gg/server/{serverId}/match/{_matchData.id}/{_matchData.current_match_map_id}/demo";

    Logger.LogInformation($"Uploading Demo {endpoint}");

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
                streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                formData.Add(streamContent, "file", Path.GetFileName(filePath));

                var response = await httpClient.PostAsync(endpoint, formData);
                if (response.IsSuccessStatusCode)
                {
                    Logger.LogInformation("File uploaded successfully.");
                    File.Delete(filePath);
                }
                else
                {
                    Logger.LogError($"File upload failed. Status code: {response.StatusCode}");
                }
            }
        }
    }
}

}
