using System.Collections.Generic;

namespace FiveStack.Entities;

public class MatchOptions
{
    public int mr { get; set; } = 0;
    public string type { get; set; } = "";
    public bool overtime { get; set; } = true;
    public int best_of { get; set; } = 1;
    public int tv_delay { get; set; } = 115;
    public int? round_restart_delay { get; set; } = null;
    public bool halftime_pausematch { get; set; } = false;
    public bool camera_required { get; set; } = false;
    public bool coaches { get; set; } = true;
    public int number_of_substitutes { get; set; } = 0;
    public bool knife_round { get; set; } = true;
    public bool? default_models { get; set; } = false;
    public string ready_setting { get; set; } = "Players";
    public string timeout_setting { get; set; } = "CoachAndPlayers";
    public string tech_timeout_setting { get; set; } = "CoachAndPlayers";
    public bool use_playcast { get; set; } = false;
    public bool show_elo_ranks { get; set; } = false;
    public Dictionary<string, string> cfg_overrides { get; set; } =
        new Dictionary<string, string>();

    // The extra layers to exec after the type cfg, in order. cfg_overrides only
    // says which files to write, and a dictionary has no order to read the
    // last-wins precedence out of, so the panel states it here instead.
    public List<string> cfg_execs { get; set; } = new List<string>();

    // Rush is run by Valve's rush_001 map script: one side for the whole match,
    // first to 8 wins with 7-7 sent to its decider room whatever mr says, and
    // the room progression lives in the script where a round backup can't
    // reach it.
    private const int RushRoundsToWin = 8;

    public bool IsRush()
    {
        return type == "Rush";
    }

    public int GameMode()
    {
        if (IsRush())
        {
            return 6;
        }

        return type == "Duel" || type == "Wingman" ? 2 : 1;
    }

    public int ExpectedPlayerCount()
    {
        switch (type)
        {
            case "Rush":
                return 6;
            case "Wingman":
                return 4;
            case "Duel":
                return 2;
            default:
                return 10;
        }
    }

    public int MaxRounds()
    {
        return IsRush() ? RushRoundsToWin * 2 - 1 : mr * 2;
    }

    public bool OvertimeEnabled()
    {
        return overtime && !IsRush();
    }

    public bool KnifeRoundEnabled()
    {
        return knife_round && !IsRush();
    }

    public bool SwapsSides()
    {
        return !IsRush();
    }

    public bool CanRestoreRounds()
    {
        return !IsRush();
    }
}
