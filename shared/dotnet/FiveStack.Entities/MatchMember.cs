namespace FiveStack.Entities;

public class MatchMember
{
    public string name { get; set; } = "";
    public string role { get; set; } = "";
    public string placeholder_name { get; set; } = "";
    public string? steam_id { get; set; } = "";
    public bool captain { get; set; } = false;
    public Guid match_lineup_id { get; set; } = Guid.Empty;
    public bool is_banned { get; set; } = false;
    public bool is_gagged { get; set; } = false;
    public bool is_muted { get; set; } = false;

    // Only meaningful when the match requires cameras. The API decides this --
    // the server cannot see a webcam feed, it is only told about one.
    public bool camera_ok { get; set; } = true;
    public int? elo { get; set; }
}
