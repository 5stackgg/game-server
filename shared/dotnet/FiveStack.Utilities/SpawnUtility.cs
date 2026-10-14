namespace FiveStack.Utilities
{
    public static class SpawnUtility
    {
        // A respawn that never reached a spawn point leaves the pawn on the
        // world origin's column, under the map. No real spawn point sits there.
        public static bool IsAtWorldOrigin(float x, float y)
        {
            return MathF.Abs(x) < 1 && MathF.Abs(y) < 1;
        }

        // A fair knife round can't run out of time with a side all dead: that
        // side would have lost on elimination. One that does never got to play.
        public static bool IsUnplayedKnifeTimeout(
            int tPlayers,
            int tAlive,
            int ctPlayers,
            int ctAlive
        )
        {
            return (tPlayers > 0 && tAlive == 0) || (ctPlayers > 0 && ctAlive == 0);
        }
    }
}
