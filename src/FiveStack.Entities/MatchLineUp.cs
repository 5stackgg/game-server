namespace FiveStack.Entities;

public class MatchLineUp
{
    public Guid id { get; set; } = Guid.Empty;
    public string name { get; set; } = "";

    public string team { get; set; } = "";
    public string coach_steam_id { get; set; } = "";

    public List<MatchMember> lineup_players { get; set; } = new List<MatchMember>();
}
