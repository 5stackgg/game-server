namespace FiveStack.Utilities
{
    public static class TeamRotation
    {
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
    }
}
