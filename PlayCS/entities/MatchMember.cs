namespace PlayCs.entities;

public class MatchMember
{
    public string name { get; set; }
    public object steam_id { get; set; }
    public MatchTeam team { get; set; }
}
