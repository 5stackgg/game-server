namespace FiveStack.Entities.Practice;

// What the panel tells a practice server about the session it is hosting: who
// may join, and the password the connect tokens are signed with. A practice
// server never loads the match plugin, so this is the only roster it has.
public class PracticeSessionData
{
    public Guid id { get; set; }
    public Guid match_id { get; set; }
    public string password { get; set; } = "";
    public string map { get; set; } = "";
    public List<string> allowed_steam_ids { get; set; } = new List<string>();

    // Null unless the panel has loaded an execute onto this session.
    public NadePlaybook? playbook { get; set; }
}
