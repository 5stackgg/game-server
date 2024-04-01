using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    public async void StartLive()
    {
        if (_matchData == null || IsLive())
        {
            return;
        }

        if (_matchData == null)
        {
            return;
        }

        _startDemoRecording();

        UpdateCurrentRound();

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

        SendCommands(new[] { "mp_autokick 0", "mp_warmup_end", "mp_restartgame 1" });

        PublishMapStatus(eMapStatus.Live);

        await Task.Delay(1000);
        Server.NextFrame(() =>
        {
            Message(HudDestination.Alert, "LIVE LIVE LIVE!");
        });
    }

    public bool IsLive()
    {
        return _currentMapStatus != eMapStatus.Unknown
            && _currentMapStatus != eMapStatus.Warmup
            && _currentMapStatus != eMapStatus.Knife;
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

        Message(HudDestination.Alert, "Recording Demo");

        SendCommands(new[] { $"tv_record /opt/demos/{GetSafeMatchPrefix()}" });
    }

    public bool IsKnife()
    {
        return _currentMapStatus == eMapStatus.Knife;
    }
}
