namespace FiveStack.entities;

public class MatchMap
{
    public Guid id { get; set; } = Guid.Empty;
    public string map { get; set; } = "";
    public int order { get; set; } = 0;
}
