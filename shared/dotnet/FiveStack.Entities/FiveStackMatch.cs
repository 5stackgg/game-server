namespace FiveStack.Entities;

public class MatchData
{
    public Guid id { get; set; } = Guid.Empty;
    public bool is_lan { get; set; } = false;
    public string password { get; set; } = "connectme";

    // Both are sent by the API on current-match/:serverId.
    public bool is_tournament_match { get; set; } = false;
    public bool is_draft_match { get; set; } = false;

    // When the API will cancel this match if it never gets going. Null once the
    // match is underway or the deadline has been cleared.
    public DateTime? cancels_at { get; set; } = null;

    public Guid? current_match_map_id { get; set; } = Guid.Empty;

    public MatchMap[] match_maps { get; set; } = new MatchMap[0];

    public MatchOptions options { get; set; } = new MatchOptions();

    public Guid lineup_1_id { get; set; } = Guid.Empty;
    public Guid lineup_2_id { get; set; } = Guid.Empty;

    public MatchLineUp lineup_1 { get; set; } = new MatchLineUp();
    public MatchLineUp lineup_2 { get; set; } = new MatchLineUp();
}
