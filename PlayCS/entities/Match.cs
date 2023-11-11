namespace PlayCs.entities;

public class Match
{
    public Guid id { get; set; } = Guid.Empty;
    public int mr { get; set; } = 12;
    public string map { get; set; } = "";
    public string status { get; set; } = "Unknown";
    public bool overtime { get; set; } = true;
    public string password { get; set; } = "connectme";
    public bool knife_round { get; set; } = true;
    public List<MatchMember?> members { get; set; } = new List<MatchMember?>();
    public List<MatchTeam> teams { get; set; } = new List<MatchTeam>();
}
