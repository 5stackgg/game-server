using FiveStack.enums;

namespace FiveStack.entities;

public class MatchMap
{
    public Guid id { get; set; } = Guid.Empty;
    public Map map { get; set; } = new Map();
    public int order { get; set; } = 0;
    public string status { get; set; } = eMapStatus.Unknown.ToString();
    public string lineup_1_side { get; set; } = "";
    public string lineup_2_side { get; set; } = "";

    public int lineup_1_timeouts_available { get; set; } = 0;
    public int lineup_2_timeouts_available { get; set; } = 0;
}
