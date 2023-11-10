namespace PlayCs.entities;

public class MatchData
{
    public Guid id { get; set; }
    public int mr { get; set; }
    public string map { get; set; }
    public string status { get; set; }
    public bool overtime { get; set; }
    public string password { get; set; }
    public bool knife_round { get; set; }
}
