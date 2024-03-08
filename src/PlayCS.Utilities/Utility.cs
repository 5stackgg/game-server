using CounterStrikeSharp.API.Modules.Utils;
using PlayCS.enums;

namespace PlayCs;

/**
 * i dont think these need to be in a class, move out later
 */
public partial class PlayCsPlugin
{
    private CsTeam TeamNumToCSTeam(int teamNum)
    {
        switch (teamNum)
        {
            case 1:
                return CsTeam.Spectator;
            case 2:
                return CsTeam.Terrorist;
            case 3:
                return CsTeam.CounterTerrorist;
            default:
                return CsTeam.None;
        }
    }

    private string TeamNumToString(int teamNum)
    {
        switch (teamNum)
        {
            case 1:
                return "Spectator";
            case 2:
                return "TERRORIST";
            case 3:
                return "CT";
            default:
                return "None";
        }
    }

    private string CSTeamToString(CsTeam team)
    {
        switch (team)
        {
            case CsTeam.Spectator:
                return "Spectator";
            case CsTeam.Terrorist:
                return "TERRORIST";
            case CsTeam.CounterTerrorist:
                return "CT";
            default:
                return "None";
        }
    }

    private CsTeam TeamStringToCsTeam(string team)
    {
        switch (team)
        {
            case "Spectator":
                return CsTeam.Spectator;
            case "TERRORIST":
                return CsTeam.Terrorist;
            case "CT":
                return CsTeam.CounterTerrorist;
            default:
                return CsTeam.None;
        }
    }

    /*
     * HitGroup_t
     */
    private string HitGroupToString(int hitGroup)
    {
        switch (hitGroup)
        {
            case 0:
                return "Body";
            case 1:
                return "Head";
            case 2:
                return "Chest";
            case 3:
                return "Stomach";
            case 4:
                return "Left Arm";
            case 5:
                return "Right Arm";
            case 6:
                return "Left Leg";
            case 7:
                return "Right Leg";
            case 10:
                return "Gear";
            default:
                return "Unknown";
        }
    }

    private eGameState GameStateStringToEnum(string state)
    {
        switch (state)
        {
            case "Finished":
                return eGameState.Finished;
            case "Knife":
                return eGameState.Knife;
            case "Live":
                return eGameState.Live;
            case "Overtime":
                return eGameState.Overtime;
            case "Paused":
                return eGameState.Paused;
            case "Scheduled":
                return eGameState.Scheduled;
            case "TechTimeout":
                return eGameState.TechTimeout;
            case "Warmup":
                return eGameState.Warmup;
            case "PickingPlayers":
                return eGameState.Warmup;
            default:
                throw new ArgumentException($"Unsupported status string: {state}");
        }
    }

    public string GetSafeMatchPrefix()
    {
        if (_matchData == null)
        {
            return "backup";
        }
        return $"{_matchData.id}".Replace("-", "");
    }
}
