using FiveStack.Entities;
using FiveStack.Enums;

namespace FiveStack.Utilities;

// A match with a halftime break waits for both teams at regulation halftime and
// before each overtime. The side swap inside an overtime is not a break.
public static class BreakUtility
{
    public const int Halftime = 0;

    // By the rounds already played at a round start: Halftime, the number of
    // the overtime about to start, or null for no break.
    public static int? BreakBefore(
        int roundsPlayed,
        int mr,
        int overtimeMaxRounds,
        bool swapsSides,
        bool overtime
    )
    {
        if (mr <= 0 || roundsPlayed <= 0)
        {
            return null;
        }

        if (roundsPlayed == mr)
        {
            return swapsSides ? Halftime : null;
        }

        if (!overtime || overtimeMaxRounds <= 0 || roundsPlayed < mr * 2)
        {
            return null;
        }

        int intoOvertime = roundsPlayed - mr * 2;

        return intoOvertime % overtimeMaxRounds == 0
            ? intoOvertime / overtimeMaxRounds + 1
            : null;
    }

    // An overtime only starts from a tied score; any other score at that round
    // count means the map is decided.
    public static bool Owed(
        bool optionOn,
        bool inPlay,
        bool restoring,
        bool taken,
        bool tied,
        int? breakNumber
    )
    {
        if (!optionOn || !inPlay || restoring || taken || breakNumber == null)
        {
            return false;
        }

        return breakNumber == Halftime || tied;
    }

    // .r readies the caller's own team, staff included; staff force the break
    // with .resume, or with .r when they are not in the match.
    public static eBreakReady WhoReadies(bool staff, bool inMatch, bool readyCommand)
    {
        if (inMatch && (readyCommand || !staff))
        {
            return eBreakReady.Team;
        }

        return staff ? eBreakReady.Force : eBreakReady.Refused;
    }

    // A lineup's players and, when the match has coaches, its coach.
    public static Guid? LineupOf(MatchData matchData, string steamId)
    {
        if (string.IsNullOrEmpty(steamId))
        {
            return null;
        }

        Guid? lineupId = LineupMembership.LineupOf(matchData, steamId, "");

        if (lineupId != null || !matchData.options.coaches)
        {
            return lineupId;
        }

        if (steamId == matchData.lineup_1.coach_steam_id)
        {
            return matchData.lineup_1_id;
        }

        if (steamId == matchData.lineup_2.coach_steam_id)
        {
            return matchData.lineup_2_id;
        }

        return null;
    }
}
