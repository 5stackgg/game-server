using FiveStack.Entities;

namespace FiveStack.Utilities
{
    public static class CommunicationAbuseMute
    {
        // The match data decides, not the voice flags: they are only applied
        // once the player is placed, and a reconnect resets them.
        public static bool ShouldClear(
            bool hasCommunicationAbuseMute,
            bool voiceMuted,
            MatchData? matchData,
            string steamId,
            string playerName
        )
        {
            if (!hasCommunicationAbuseMute || voiceMuted || matchData == null)
            {
                return false;
            }

            return LineupMembership.Member(matchData, steamId, playerName)?.is_muted != true;
        }
    }
}
