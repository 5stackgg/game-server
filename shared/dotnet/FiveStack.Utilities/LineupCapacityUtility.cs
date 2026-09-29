namespace FiveStack.Utilities
{
    public static class LineupCapacityUtility
    {
        private const int TerroristTeamNum = 2;
        private const int CounterTerroristTeamNum = 3;

        // Counted by lineup, not by the team the joiner is headed for: through a
        // halftime swap the other lineup still stands on that team until the
        // round resets.
        public static bool IsOverCapacity(
            IEnumerable<(string SteamId, Guid? LineupId, int TeamNum)> connected,
            string joinerSteamId,
            Guid? joinerLineupId,
            int placementTeamNum,
            int capacity
        )
        {
            if (joinerLineupId == null || !IsPlaying(placementTeamNum))
            {
                return false;
            }

            int playing = connected.Count(player =>
                player.SteamId != joinerSteamId
                && player.LineupId == joinerLineupId
                && IsPlaying(player.TeamNum)
            );

            return playing >= capacity;
        }

        private static bool IsPlaying(int teamNum)
        {
            return teamNum == TerroristTeamNum || teamNum == CounterTerroristTeamNum;
        }
    }
}
