using FiveStack.Entities;
using FiveStack.Enums;

namespace FiveStack.Utilities;

public static class LeaverRuleUtility
{
    // A LAN or offline drop is a dead machine or network far more often than a
    // leaver, so it only counts as an abandon once the short team has given the
    // map up over it. A LAN tournament match is never given up: its organizers
    // are told instead. Offline nothing reaches an organizer, so offline wins.
    public static eLeaverRule For(MatchData? matchData, bool offline)
    {
        if (offline)
        {
            return eLeaverRule.AbandonOnSurrender;
        }

        if (matchData?.is_lan != true)
        {
            return eLeaverRule.Abandon;
        }

        return matchData.is_tournament_match
            ? eLeaverRule.NoSurrender
            : eLeaverRule.AbandonOnSurrender;
    }
}
