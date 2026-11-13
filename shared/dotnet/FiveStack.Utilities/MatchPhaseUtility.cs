using FiveStack.Enums;

namespace FiveStack.Utilities;

public static class MatchPhaseUtility
{
    public static eMatchPhase PhaseOf(eMapStatus status)
    {
        switch (status)
        {
            case eMapStatus.Scheduled:
            case eMapStatus.Warmup:
                return eMatchPhase.Warmup;
            case eMapStatus.Knife:
                return eMatchPhase.Knife;
            case eMapStatus.Live:
            case eMapStatus.Overtime:
            case eMapStatus.Paused:
                return eMatchPhase.InPlay;
            case eMapStatus.WaitingForTV:
            case eMapStatus.UploadingDemo:
            case eMapStatus.Finished:
            case eMapStatus.Surrendered:
                return eMatchPhase.Over;
            default:
                return eMatchPhase.None;
        }
    }

    // Only a new map leaves the end of one: a vote or a stale status from the api
    // must not take a map that is over back into play, or surrender it.
    public static bool Reopens(eMapStatus current, eMapStatus next)
    {
        if (PhaseOf(current) != eMatchPhase.Over || current == next)
        {
            return false;
        }

        eMatchPhase nextPhase = PhaseOf(next);

        return next == eMapStatus.Surrendered
            || nextPhase == eMatchPhase.Warmup
            || nextPhase == eMatchPhase.Knife
            || nextPhase == eMatchPhase.InPlay;
    }

    // The wait for the broadcast is a timer in the process that ended the map,
    // so a server that starts into that wait has nothing to finish the map.
    public static bool StartsIntoTvWait(eMapStatus known, eMapStatus reported)
    {
        return known == eMapStatus.Unknown && reported == eMapStatus.WaitingForTV;
    }
}
