using FiveStack.Entities;

namespace FiveStack.Utilities
{
    public static class TeamRotation
    {
        private const int TerroristTeamNum = 2;
        private const int CounterTerroristTeamNum = 3;

        public static bool IsOnOppositeSide(int round, int mr, int overtimeMr)
        {
            if (round < mr * 2)
            {
                return round >= mr;
            }

            int overtimeRound = round - (mr * 2);
            int overTimeNumber = (overtimeRound / overtimeMr) + 1;
            int block = overtimeRound % overtimeMr;

            if (overTimeNumber % 2 == 1)
            {
                return block < (overtimeMr / 2);
            }

            return block >= (overtimeMr / 2);
        }

        // A majority out of place means our side model is what's wrong, not the players.
        public static bool ShouldReconcile(int mismatched, int placed)
        {
            return mismatched > 0 && mismatched * 2 < placed;
        }

        // A pending halftime swap flips T/CT players at the round reset, so a player
        // the swap will carry must join the side opposite the one they should end on.
        public static int PlacementSide(int expectedTeamNum, bool switchingAtReset)
        {
            if (!switchingAtReset)
            {
                return expectedTeamNum;
            }

            switch (expectedTeamNum)
            {
                case TerroristTeamNum:
                    return CounterTerroristTeamNum;
                case CounterTerroristTeamNum:
                    return TerroristTeamNum;
                default:
                    return expectedTeamNum;
            }
        }

        public static void SwapLineupSides(MatchMap currentMap)
        {
            currentMap.lineup_1_side = GetOppositeSideName(currentMap.lineup_1_side);
            currentMap.lineup_2_side = GetOppositeSideName(currentMap.lineup_2_side);
        }

        private static string GetOppositeSideName(string side)
        {
            switch (side)
            {
                case "CT":
                    return "TERRORIST";
                case "TERRORIST":
                    return "CT";
                default:
                    return side;
            }
        }
    }
}
