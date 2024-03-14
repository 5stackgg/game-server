namespace FiveStack.entities;

public class MatchLineUp
{
    public Guid id { get; set; } = Guid.Empty;
    public string name { get; set; } = "";

    public List<MatchMember> lineup_players { get; set; } = new List<MatchMember>();
}
