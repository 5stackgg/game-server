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

    // A drop outside play ends in neither a vote nor an abandon, so only a LAN
    // tournament match times one: its organizers are told about a player gone
    // from warmup, knife or the wait for the next map too, until the map is over.
    public static bool TimesLeaver(eLeaverRule rule, bool inPlay, bool mapOver)
    {
        return inPlay || (rule == eLeaverRule.NoSurrender && !mapOver);
    }

    // A player who left where leaving was not timed has no disconnect left to
    // time them, so they are timed once the map leaves its warmup or its knife
    // round. A LAN tournament also times them from the warmup of every map
    // after the first, for a player who left while the last map was over.
    public static bool TimesAbsentPlayers(
        eLeaverRule rule,
        eMapStatus current,
        eMapStatus next,
        int mapOrder
    )
    {
        eMatchPhase from = MatchPhaseUtility.PhaseOf(current);
        eMatchPhase to = MatchPhaseUtility.PhaseOf(next);

        if (to == eMatchPhase.Knife || to == eMatchPhase.InPlay)
        {
            return from == eMatchPhase.Warmup || from == eMatchPhase.Knife;
        }

        return rule == eLeaverRule.NoSurrender
            && to == eMatchPhase.Warmup
            && from != eMatchPhase.Warmup
            && mapOrder > 1;
    }
}
