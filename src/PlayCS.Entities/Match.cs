namespace PlayCs.entities;

public class Match
{
    public Guid id { get; set; } = Guid.Empty;
    public int mr { get; set; } = 12;
    public string map { get; set; } = "";
    public string type { get; set; } = "";
    public string status { get; set; } = "Unknown";
    public bool overtime { get; set; } = true;
    public string password { get; set; } = "connectme";
    public bool knife_round { get; set; } = true;

    public MatchLineUp lineup_1 { get; set; } = new MatchLineUp();
    public MatchLineUp lineup_2 { get; set; } = new MatchLineUp();
}
