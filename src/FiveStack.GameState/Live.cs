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

        Logger.LogInformation($"Found Backup Round File {highestNumber} and were on {_currentRound}");

        if (_currentRound > 0 && _currentRound >= highestNumber)
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
        // TODO - check if we are already recording
        if (_matchData == null)
        {
            return;
        }

        Message(HudDestination.Alert, "Recording Demo");

        SendCommands(new[] { $"tv_record /opt/demos/{GetSafeMatchPrefix()}" });
    }
}
