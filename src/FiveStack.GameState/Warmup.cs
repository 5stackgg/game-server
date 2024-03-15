using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    private Dictionary<int, bool> _readyPlayers = new Dictionary<int, bool>();

    public void StartWarmup()
    {
        if (_matchData == null || IsLive())
        {
            return;
        }

        _resetCaptains();
        _resetReadyPlayers();

        if (_matchData.type == "Wingman")
        {
            SendCommands(new[] { "game_type 0; game_mode 2" });
        }
        else
        {
            SendCommands(new[] { "game_type 0; game_mode 1" });
        }

        SendCommands(
            new[]
            {
                "bot_kick",
                "mp_autokick 0",
                "mp_limitteams 0",
                "mp_autoteambalance 0",
                "sv_disable_teamselect_menu 0",
                "mp_warmuptime_all_players_connected 0",
                "mp_roundtime 60",
                "mp_roundtime_defuse 0",
                "mp_ct_default_secondary weapon_hkp2000",
                "mp_t_default_secondary weapon_glock",
                "mp_give_player_c4 0",
                "mp_warmup_pausetimer 1",
                "mp_maxmoney 60000",
                "mp_startmoney 60000",
                "mp_free_armor 1",
                "mp_warmup_start",
            }
        );

        _publishGameState(eMapStatus.Warmup);
    }

    public bool IsWarmup()
    {
        CCSGameRules? rules = _gameRules();

        if (rules == null)
        {
            return false;
        }

        return rules.WarmupPeriod;
    }

    private void _resetReadyPlayers()
    {
        _readyPlayers = new Dictionary<int, bool>();
    }

    private void _resetCaptains()
    {
        _captains[CsTeam.Terrorist] = null;
        _captains[CsTeam.CounterTerrorist] = null;
    }

    public int TotalReady()
    {
        return _readyPlayers.Count(pair => pair.Value);
    }

    private CCSGameRules? _gameRules()
    {
        try
        {
            return Utilities
                .FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .First()
                .GameRules;
        }
        catch
        {
            // do nothing
        }
        return null;
    }
}
