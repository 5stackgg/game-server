namespace FiveStack.Entities;

public class MatchData
{
    public Guid id { get; set; } = Guid.Empty;
    public bool is_lan { get; set; } = false;
    public string password { get; set; } = "connectme";

    // Both are sent by the API on current-match/:serverId.
    public bool is_tournament_match { get; set; } = false;
    public bool is_draft_match { get; set; } = false;

    // Older APIs omit this and route every chat event into the shared match
    // room, so team chat must only be relayed when the API advertises it.
    public bool relay_team_chat { get; set; } = false;

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

    // Two sides of the type's size, unless both lineups are smaller, as in a
    // 3v3 Competitive: counting the type's players would hold the match for
    // players who don't exist.
    public int ExpectedPlayerCount()
    {
        int side = options.ExpectedPlayerCount() / 2;
        int largest = Math.Max(lineup_1.lineup_players.Count, lineup_2.lineup_players.Count);

        return 2 * (largest == 0 ? side : Math.Min(side, largest));
    }
}
