namespace FiveStack.Entities;

public class MatchOptions
{
    public int mr { get; set; } = 0;
    public string type { get; set; } = "";
    public bool overtime { get; set; } = true;
    public int best_of { get; set; } = 1;
    public int tv_delay { get; set; } = 115;
    public bool coaches { get; set; } = true;
    public int number_of_substitutes { get; set; } = 0;
    public bool knife_round { get; set; } = true;
    public string ready_setting { get; set; } = "Players";
    public string timeout_setting { get; set; } = "CoachAndPlayers";
    public string tech_timeout_setting { get; set; } = "CoachAndPlayers";
    public string cfg_override { get; set; } = "";
}
