using FiveStack.Entities;

namespace FiveStack.Simulator;

public enum Side
{
    None,
    T,
    CT,
}

// The plugin's TeamUtility and CS2's own win rules, without the engine.
public static class SideRules
{
    public const int OvertimeMaxRounds = 6;

    public static string ToApi(Side side)
    {
        return side switch
        {
            Side.T => "TERRORIST",
            Side.CT => "CT",
            _ => "None",
        };
    }

    public static Side FromApi(string? side)
    {
        return side switch
        {
            "TERRORIST" => Side.T,
            "CT" => Side.CT,
            _ => Side.None,
        };
    }

    public static Side Opposite(Side side)
    {
        return side switch
        {
            Side.T => Side.CT,
            Side.CT => Side.T,
            _ => Side.None,
        };
    }

    // roundsPlayed is the number of rounds finished before the one asked about.
    public static Side LineupSide(MatchOptions options, Side startingSide, int roundsPlayed)
    {
        int mr = options.mr;

        if (mr <= 0 || startingSide == Side.None)
        {
            return Side.None;
        }

        if (!options.SwapsSides())
        {
            return startingSide;
        }

        if (roundsPlayed < mr * 2)
        {
            return roundsPlayed < mr ? startingSide : Opposite(startingSide);
        }

        int overtimeRound = roundsPlayed - mr * 2;
        int overtimeNumber = overtimeRound / OvertimeMaxRounds + 1;
        int block = overtimeRound % OvertimeMaxRounds;

        bool swapped =
            overtimeNumber % 2 == 1
                ? block < OvertimeMaxRounds / 2
                : block >= OvertimeMaxRounds / 2;

        return swapped ? Opposite(startingSide) : startingSide;
    }

    public static bool IsMapOver(MatchOptions options, int lineup1Score, int lineup2Score)
    {
        int maxRounds = options.MaxRounds();
        int played = lineup1Score + lineup2Score;
        int leader = Math.Max(lineup1Score, lineup2Score);

        if (played <= maxRounds)
        {
            if (leader >= maxRounds / 2 + 1)
            {
                return true;
            }

            return played == maxRounds
                && !(options.OvertimeEnabled() && lineup1Score == lineup2Score);
        }

        int block = (played - maxRounds - 1) / OvertimeMaxRounds;
        int blockStart = maxRounds / 2 + block * (OvertimeMaxRounds / 2);

        return leader >= blockStart + OvertimeMaxRounds / 2 + 1;
    }

    public static bool IsOvertime(MatchOptions options, int roundsPlayed)
    {
        return options.OvertimeEnabled() && roundsPlayed >= options.MaxRounds();
    }

    public static bool SidesSwitch(MatchOptions options, int roundsPlayed)
    {
        return roundsPlayed > 0
            && LineupSide(options, Side.CT, roundsPlayed)
                != LineupSide(options, Side.CT, roundsPlayed - 1);
    }

    // Money goes back to the start amount at halftime and at the start of
    // every overtime half.
    public static bool ResetsMoney(MatchOptions options, int roundsPlayed)
    {
        int maxRounds = options.MaxRounds();

        if (roundsPlayed == 0 || !options.SwapsSides())
        {
            return false;
        }

        if (roundsPlayed < maxRounds)
        {
            return roundsPlayed == options.mr;
        }

        return options.OvertimeEnabled()
            && (roundsPlayed - maxRounds) % (OvertimeMaxRounds / 2) == 0;
    }
}
