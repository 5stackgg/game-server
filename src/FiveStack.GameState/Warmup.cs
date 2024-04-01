using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    public void StartWarmup()
    {
        if (_matchData == null)
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

        SendCommands(new[] { "exec warmup", "mp_warmup_start" });

        PublishMapStatus(eMapStatus.Warmup);
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
