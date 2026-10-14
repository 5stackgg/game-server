namespace FiveStack.Utilities
{
    public static class KnifeUtility
    {
        // A fair knife round can't run out of time with a side all dead: that
        // side would have lost on elimination. One that does never got to play.
        public static bool IsUnplayedTimeout(int tPlayers, int tAlive, int ctPlayers, int ctAlive)
        {
            return (tPlayers > 0 && tAlive == 0) || (ctPlayers > 0 && ctAlive == 0);
        }
    }
}
