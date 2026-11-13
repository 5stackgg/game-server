using FiveStack.Entities;

namespace FiveStack.Utilities;

public static class ReconnectPauseUtility
{
    public static bool ShouldPause(
        MatchData matchData,
        IEnumerable<(string SteamId, string Name)> connected,
        int expectedPlayers
    )
    {
        return connected.Count() < expectedPlayers;
    }
}
