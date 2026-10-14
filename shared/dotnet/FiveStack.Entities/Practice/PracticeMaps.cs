namespace FiveStack.Entities.Practice;

// What GET /utility/practice-maps answers: every map the server can be moved
// to, the one it is on, and whether the player asking may move it.
public class PracticeMaps
{
    public string map_name { get; set; } = "";
    public bool can_change { get; set; }
    public List<string> maps { get; set; } = new List<string>();
}
