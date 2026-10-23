namespace FiveStack.Utilities;

public static class PlayerPresenceUtility
{
    // A lineup member is reported leaving even if their connect never was:
    // they may have joined before the match loaded.
    public static bool ShouldReportDisconnect(
        HashSet<ulong> reportedConnected,
        ulong steamId,
        bool isLineupMember
    )
    {
        return reportedConnected.Remove(steamId) || isLineupMember;
    }
}
