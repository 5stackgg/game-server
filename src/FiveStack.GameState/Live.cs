using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    public async void StartLive()
    {
        if (_matchData == null)
        {
            return;
        }

        _startDemoRecording();

        UpdateCurrentRound();

        SendCommands(
            new[]
            {
                $"mp_backup_round_file {GetSafeMatchPrefix()}",
                "mp_round_restart_delay 3",
                "mp_free_armor 0",
                "mp_give_player_c4 1",
                "mp_maxmoney 16000",
                "mp_roundtime 1.92",
                "mp_roundtime_defuse 1.92",
                "mp_freezetime 15",
                "mp_startmoney 800",
                "mp_ct_default_secondary weapon_hkp2000",
                "mp_t_default_secondary weapon_glock",
                "mp_spectators_max 0",
                "sv_disable_teamselect_menu 1",
                // OT settings
                $"mp_overtime_enable {_matchData.overtime}",
                "mp_overtime_startmoney 10000",
                "mp_overtime_maxrounds 6",
                "mp_overtime_halftime_pausetimer 0",
                "cash_team_bonus_shorthanded 0",
                // MR settings
                $"mp_maxrounds {_matchData.mr * 2}",
                "mp_autoteambalance 0",
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
            _publishGameState(eGameState.Live);
            return;
        }

        SendCommands(new[] { "bot_kick", "mp_autokick 0", "mp_warmup_end", "mp_restartgame 1" });

        // TODO - server crashes , we dont know this info
        TeamTimeouts[CsTeam.Terrorist] = 0;
        TeamTimeouts[CsTeam.CounterTerrorist] = 0;

        _publishGameState(eGameState.Live);

        await Task.Delay(1000);
        Server.NextFrame(() =>
        {
            Message(HudDestination.Alert, "LIVE LIVE LIVE!");
        });
    }

    public bool IsLive()
    {
        return _currentGameState != eGameState.Unknown
            && _currentGameState != eGameState.Warmup
            && _currentGameState != eGameState.Knife
            && _currentGameState != eGameState.Scheduled;
    }

    private void _startDemoRecording()
    {
        if (_matchData == null)
        {
            return;
        }

        SendCommands(new[] { $"tv_record /opt/demos/{GetSafeMatchPrefix()}" });
    }

    public bool IsKnife()
    {
        return _currentGameState == eGameState.Knife;
    }
}
