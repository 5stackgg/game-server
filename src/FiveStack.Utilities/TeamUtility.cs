using System.Linq;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;

namespace FiveStack.Utilities
{
    public static class TeamUtility
    {
        public static CsTeam GetLineupSide(
            MatchData matchData,
            MatchMap currentMap,
            Guid lineupId,
            int round
        )
        {
            if (matchData == null || currentMap == null)
            {
                return CsTeam.None;
            }

            int mr = matchData.options.mr;
            if (mr <= 0)
            {
                return CsTeam.None;
            }

            // Determine if this is lineup_1 or lineup_2
            bool isLineup1 = matchData.lineup_1_id == lineupId;
            CsTeam startingSide = TeamStringToCsTeam(
                isLineup1 ? currentMap.lineup_1_side : currentMap.lineup_2_side
            );

            if (startingSide == CsTeam.None || startingSide == CsTeam.Spectator)
            {
                return CsTeam.None;
            }

            // Calculate which side based on round number
            // Regular time: rounds 0 to (MR-1) on starting side, rounds MR to (MR*2-1) on opposite side
            if (round < mr * 2)
            {
                if (round < mr)
                {
                    // First half: on starting side
                    return startingSide;
                }

                // Second half: on opposite side
                return GetOppositeSide(startingSide);
            }
            else
            {
                // Overtime: rounds >= MR*2
                int overtimeRound = round - (mr * 2);
                int overtimeMr =
                    ConVar.Find("mp_overtime_maxrounds")?.GetPrimitiveValue<int>() ?? 6;

                // Calculate which OT half (1-indexed) and position within that half
                int overTimeNumber = (overtimeRound / overtimeMr) + 1;
                int block = overtimeRound % overtimeMr;

                return (
                    overTimeNumber % 2 == 1 ? block < (overtimeMr / 2) : block >= (overtimeMr / 2)
                )
                    ? GetOppositeSide(startingSide)
                    : startingSide;
            }
        }

        private static CsTeam GetOppositeSide(CsTeam side)
        {
            switch (side)
            {
                case CsTeam.Terrorist:
                    return CsTeam.CounterTerrorist;
                case CsTeam.CounterTerrorist:
                    return CsTeam.Terrorist;
                default:
                    return CsTeam.None;
            }
        }

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

        public static int GetTeamScore(
            MatchData matchData,
            MatchMap currentMap,
            Guid lineupId,
            int round
        )
        {
            CsTeam expectedSide = GetLineupSide(matchData, currentMap, lineupId, round);
            if (expectedSide == CsTeam.None)
            {
                return 0;
            }

            foreach (var team in MatchUtility.Teams())
            {
                if (TeamUtility.TeamNumToCSTeam(team.TeamNum) == expectedSide)
                {
                    return team.Score;
                }
            }

            return 0;
        }

        public static int GetTeamMoney(
            MatchData matchData,
            MatchMap currentMap,
            Guid lineupId,
            int round
        )
        {
            CsTeam expectedSide = GetLineupSide(matchData, currentMap, lineupId, round);
            if (expectedSide == CsTeam.None)
            {
                return 0;
            }

            int totalCash = 0;

            foreach (var team in MatchUtility.Teams())
            {
                if (TeamUtility.TeamNumToCSTeam(team.TeamNum) == expectedSide)
                {
                    foreach (var player in team.PlayerControllers)
                    {
                        totalCash += (
                            CounterStrikeSharp
                                .API.Utilities.GetPlayerFromIndex((int)player.Index)
                                ?.InGameMoneyServices?.Account
                            ?? 0
                        );
                    }
                    break; // Found the team, no need to continue
                }
            }

            return totalCash;
        }

        public static int GetTeamCount(CsTeam csTeam)
        {
            return MatchUtility
                .Teams()
                .Count(matchTeam =>
                    matchTeam.PlayerControllers.Count > 0 && matchTeam.TeamNum == (int)csTeam
                );
        }
    }
}
