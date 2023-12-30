namespace PlayCs.entities;

public class MatchMember
{
    public string name { get; set; } = "";
    public string steam_id { get; set; } = "";
    public Guid match_lineup_id { get; set; } = Guid.Empty;
}
