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

    // What this server says about Steam's relay, as SteamRelayUtility works it
    // out for every plugin.
    public bool on_steam_relay { get; set; }
    public string? steam_relay { get; set; }

    // A community server runs no match plugin, so this sync is the only thing
    // that can tell the panel it is asleep rather than gone.
    public bool hibernating { get; set; }

    // Null, not empty, while the roster is unknown or a map change is under
    // way: the panel leaves its sessions alone for null and closes them for [].
    public List<RosterPlayer>? players { get; set; }
    public List<DepartedPlayer> departed { get; set; } = new();

    // The map the players' counts were made on. Ignored by panels that predate
    // per-map sessions.
    public string? map { get; set; }
}

// Kills, deaths and the counters after them are running totals for the
// connection named by conn, which is new each time a player joins, so the
// panel never has to guess at one.
public class RosterPlayer
{
    public string steam_id { get; set; } = "";
    public string conn { get; set; } = "";
    public string name { get; set; } = "";
    public string? ip { get; set; }
    public int kills { get; set; }
    public int deaths { get; set; }

    // Ignored by panels that predate the community counters.
    public int assists { get; set; }
    public int headshots { get; set; }
    public int damage { get; set; }
    public int rounds_played { get; set; }
    public int rounds_won { get; set; }
    public int mvps { get; set; }
}

public class DepartedPlayer
{
    public string steam_id { get; set; } = "";
    public string conn { get; set; } = "";
    public int kills { get; set; }
    public int deaths { get; set; }

    // Ignored by panels that predate the community counters.
    public int assists { get; set; }
    public int headshots { get; set; }
    public int damage { get; set; }
    public int rounds_played { get; set; }
    public int rounds_won { get; set; }
    public int mvps { get; set; }
}

public class PlayerSanctionsResponse
{
    public List<PlayerSanction> sanctions { get; set; } = new();

    // Absent from panels that predate access lists.
    public ServerAccessSync? access { get; set; }

    // Absent from panels that predate the roster.
    public bool? roster_recorded { get; set; }
}
