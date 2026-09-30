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
}

public class PlayerSanctionsResponse
{
    public List<PlayerSanction> sanctions { get; set; } = new();

    // Absent from panels that predate access lists.
    public ServerAccessSync? access { get; set; }
}
