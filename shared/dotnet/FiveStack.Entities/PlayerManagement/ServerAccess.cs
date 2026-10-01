namespace FiveStack.Entities.PlayerManagement;

// What the sync says about the server's access list on every answer: whether
// it is restricted, the version of the list, and which of the players the
// plugin asked about are not on it.
public class ServerAccessSync
{
    public bool restricted { get; set; }
    public string version { get; set; } = "";
    public List<string> denied { get; set; } = new();
    public string? message { get; set; }
}

public class ServerAccessList
{
    public bool restricted { get; set; }
    public string version { get; set; } = "";
    public List<string> steam_ids { get; set; } = new();
}
