using CounterStrikeSharp.API.Modules.Utils;

namespace FiveStack.Utilities
{
    public static class TeamUtility
    {
        public static string TeamNumToString(int teamNum)
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

        public static CsTeam TeamNumToCSTeam(int teamNum)
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

        public static CsTeam TeamStringToCsTeam(string team)
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

        public static string CSTeamToString(CsTeam team)
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
    }
}
