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
    }
}
