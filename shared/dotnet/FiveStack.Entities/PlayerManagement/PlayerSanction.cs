namespace FiveStack.Entities.PlayerManagement;

public class PlayerSanction
{
    public string steam_id { get; set; } = "";
    public string type { get; set; } = "";
    public string? reason { get; set; }
    public DateTimeOffset? expires_at { get; set; }
}

public class PlayerSanctionsRequest
{
    public List<string> steam_ids { get; set; } = new();
    public string plugin_version { get; set; } = "";
    public string plugin_runtime { get; set; } = "";

    // Null, not empty, while the roster is unknown or a map change is under
    // way: the panel leaves its sessions alone for null and closes them for [].
    public List<RosterPlayer>? players { get; set; }
    public List<DepartedPlayer> departed { get; set; } = new();
}

// Kills and deaths are running totals for the connection named by conn, which
// is new each time a player joins, so the panel never has to guess at one.
public class RosterPlayer
{
    public string steam_id { get; set; } = "";
    public string conn { get; set; } = "";
    public string name { get; set; } = "";
    public string? ip { get; set; }
    public int kills { get; set; }
    public int deaths { get; set; }
}

public class DepartedPlayer
{
    public string steam_id { get; set; } = "";
    public string conn { get; set; } = "";
    public int kills { get; set; }
    public int deaths { get; set; }
}

public class PlayerSanctionsResponse
{
    public List<PlayerSanction> sanctions { get; set; } = new();

    // Absent from panels that predate access lists.
    public ServerAccessSync? access { get; set; }

    // Absent from panels that predate the roster.
    public bool? roster_recorded { get; set; }
}
