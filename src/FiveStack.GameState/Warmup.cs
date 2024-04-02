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

        ResetCaptains();
        ResetReadyPlayers();

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

    private void ResetReadyPlayers()
    {
        _readyPlayers = new Dictionary<int, bool>();
    }

    private void ResetCaptains()
    {
        _captains[CsTeam.Terrorist] = null;
        _captains[CsTeam.CounterTerrorist] = null;
    }   
}
