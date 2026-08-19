namespace FiveStack.Entities.Practice;

// The body of GET /nades/session, exactly as the API returns it.
//
// The API's spelling and the plugin's model have drifted apart more than once
// (session_id/id, map_name/map, steam_ids/allowed_steam_ids), and the failure
// mode is silent: an unparsed roster reads as "nobody is allowed" rather than
// as an error. Both spellings are accepted here so a rename on either side
// cannot empty the door policy.
public class NadeSessionRow
{
    public string? id { get; set; }
    public string? session_id { get; set; }
    public string? match_id { get; set; }
    public string? password { get; set; }
    public string? map { get; set; }
    public string? map_name { get; set; }
    public List<string>? steam_ids { get; set; }
    public List<string>? allowed_steam_ids { get; set; }
    public NadePlaybook? playbook { get; set; }

    public PracticeSessionData ToSession()
    {
        return new PracticeSessionData
        {
            id = Id(session_id ?? id),
            match_id = Id(match_id),
            password = password ?? "",
            map = map_name ?? map ?? "",
            allowed_steam_ids = steam_ids ?? allowed_steam_ids ?? new List<string>(),
            playbook = playbook,
        };
    }

    private static Guid Id(string? value)
    {
        return Guid.TryParse(value, out Guid parsed) ? parsed : Guid.Empty;
    }
}
