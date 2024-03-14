namespace FiveStack.entities;

public class Match
{
    public Guid id { get; set; } = Guid.Empty;
    public int mr { get; set; } = 0;
    public string type { get; set; } = "";
    public string status { get; set; } = "Unknown";
    public bool overtime { get; set; } = true;
    public string password { get; set; } = "connectme";
    public bool knife_round { get; set; } = true;

    public Guid current_match_map_id { get; set; } = Guid.Empty;

    public MatchMap[] match_maps { get; set; } = new MatchMap[0];

    public MatchLineUp lineup_1 { get; set; } = new MatchLineUp();
    public MatchLineUp lineup_2 { get; set; } = new MatchLineUp();
}
