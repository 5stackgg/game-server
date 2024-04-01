using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
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

        if (IsLive())
        {
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

            Logger.LogInformation($"Found Backup Round File {highestNumber}");

            if (highestNumber > 0)
            {
                RestoreBackupRound(highestNumber.ToString(), true);
                return;
            }

            // TODO - look up backup round files, if our current round is behind the newest backup we crahsed...
            // _startDemoRecording();

            return;
        }

        SendCommands(
            new[]
            {
                $"mp_maxrounds {_matchData.mr * 2}",
                $"mp_overtime_enable {_matchData.overtime}",
                $"mp_backup_round_file {GetSafeMatchPrefix()}",
                $"exec live",
            }
        );

        if (_matchData.type == "Wingman")
        {
            SendCommands(new[] { "game_type 0; game_mode 2" });
        }
        else
        {
            SendCommands(new[] { "game_type 0; game_mode 1" });
        }

        // require game state coming from Warmup / Knife
        if (!IsKnife() && !IsWarmup())
        {
            return;
        }

        SendCommands(new[] { "mp_warmup_end", "mp_restartgame 0" });

        PublishMapStatus(eMapStatus.Live);

        await Task.Delay(1000);
        Server.NextFrame(() =>
        {
            Message(HudDestination.Alert, "LIVE LIVE LIVE!");
        });
    }

    public bool isOverTime()
    {
        return getOverTimeNumber() > 0;
    }

    public int getOverTimeNumber()
    {
        CCSGameRules? rules = _gameRules();

        if (rules == null)
        {
            return 0;
        }
        return rules.OvertimePlaying;
    }

    private void _startDemoRecording()
    {
        if (_matchData == null)
        {
            return;
        }
        // TODO - check if we are already recording

        Message(HudDestination.Alert, "Recording Demo");

        SendCommands(new[] { $"tv_record /opt/demos/{GetSafeMatchPrefix()}" });
    }
}
