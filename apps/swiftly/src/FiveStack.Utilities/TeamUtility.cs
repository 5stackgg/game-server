using FiveStack.Entities;
using SwiftlyS2.Shared.Players;

namespace FiveStack.Utilities
{
    public static class TeamUtility
    {
        public static Team GetLineupSide(
            MatchData matchData,
            MatchMap currentMap,
            Guid lineupId,
            int round
        )
        {
            if (matchData == null || currentMap == null)
            {
                return Team.None;
            }

            int mr = matchData.options.mr;
            if (mr <= 0)
            {
                return Team.None;
            }

            bool isLineup1 = matchData.lineup_1_id == lineupId;
            Team startingSide = TeamStringToTeam(
                isLineup1 ? currentMap.lineup_1_side : currentMap.lineup_2_side
            );

            if (startingSide == Team.None || startingSide == Team.Spectator)
            {
                return Team.None;
            }

            int overtimeMr =
                MatchUtility.Core.ConVar.Find<int>("mp_overtime_maxrounds")?.Value ?? 6;

            return TeamRotation.IsOnOppositeSide(round, mr, overtimeMr)
                ? GetOppositeSide(startingSide)
                : startingSide;
        }

        private static Team GetOppositeSide(Team side)
        {
            switch (side)
            {
                case Team.T:
                    return Team.CT;
                case Team.CT:
                    return Team.T;
                default:
                    return Team.None;
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

        public static Team TeamNumToTeam(int teamNum)
        {
            switch (teamNum)
            {
                case 1:
                    return Team.Spectator;
                case 2:
                    return Team.T;
                case 3:
                    return Team.CT;
                default:
                    return Team.None;
            }
        }

        public static Team TeamStringToTeam(string team)
        {
            switch (team)
            {
                case "Spectator":
                    return Team.Spectator;
                case "TERRORIST":
                    return Team.T;
                case "CT":
                    return Team.CT;
                default:
                    return Team.None;
            }
        }

        public static string TeamToString(Team team)
        {
            switch (team)
            {
                case Team.Spectator:
                    return "Spectator";
                case Team.T:
                    return "TERRORIST";
                case Team.CT:
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
            Team expectedSide = GetLineupSide(matchData, currentMap, lineupId, round);
            if (expectedSide == Team.None)
            {
                return 0;
            }

            foreach (var team in MatchUtility.Teams())
            {
                if (TeamNumToTeam(team.TeamNum) == expectedSide)
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
            Team expectedSide = GetLineupSide(matchData, currentMap, lineupId, round);
            if (expectedSide == Team.None)
            {
                return 0;
            }

            int totalCash = 0;

            foreach (var player in MatchUtility.Core.PlayerManager.GetInTeam(expectedSide))
            {
                if (player.Controller == null)
                {
                    continue;
                }

                totalCash += player.Controller.InGameMoneyServices?.Account ?? 0;
            }

            return totalCash;
        }

        public static int GetTeamCount(Team team)
        {
            return MatchUtility.Core.PlayerManager.GetInTeam(team).Any() ? 1 : 0;
        }
    }
}
