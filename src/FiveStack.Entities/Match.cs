using FiveStack.enums;

namespace FiveStack.entities;

public class Match
{
    public Guid id { get; set; } = Guid.Empty;
    public int mr { get; set; } = 0;
    public string type { get; set; } = "";
    public bool overtime { get; set; } = true;
    public string password { get; set; } = "connectme";
    public bool knife_round { get; set; } = true;
    public int best_of { get; set; } = 1;

    public Guid current_match_map_id { get; set; } = Guid.Empty;

    public MatchMap[] match_maps { get; set; } = new MatchMap[0];

    public Guid lineup_1_id { get; set; } = Guid.Empty;
    public Guid lineup_2_id { get; set; } = Guid.Empty;

    public MatchLineUp lineup_1 { get; set; } = new MatchLineUp();
    public MatchLineUp lineup_2 { get; set; } = new MatchLineUp();
}
