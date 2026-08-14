using FiveStack.Enums;

namespace FiveStack.Entities;

public class MatchMap
{
    public Guid id { get; set; } = Guid.Empty;
    public Map map { get; set; } = new Map();
    public int order { get; set; } = 0;
    public string status { get; set; } = eMapStatus.Unknown.ToString();
    public string lineup_1_side { get; set; } = "";
    public string lineup_2_side { get; set; } = "";

    // Null until the map is decided. Sent by the API on current-match/:serverId.
    public Guid? winning_lineup_id { get; set; } = null;

    public BackupRound[] rounds { get; set; } = new BackupRound[0];
}
