namespace FiveStack.Utilities;

// What the panel asked for when it told a practice server to change map, and
// what to hand somebody once the new level is up.
public class PracticeMapChangeRequest
{
    // Either a map name or a workshop id -- the panel resolved which, and the
    // command it produces is different for each.
    public string map { get; set; } = string.Empty;

    public ulong steam_id { get; set; }

    public IReadOnlyList<string> lineup_ids { get; set; } = Array.Empty<string>();

    public bool HasLoad => steam_id != 0 && lineup_ids.Count > 0;
}

// A load that has to survive a changelevel. The plugin instance does survive
// one, so this is held in memory across the level change and consumed on the
// other side.
public class PracticeMapChangePending
{
    public ulong steam_id { get; set; }

    public IReadOnlyList<string> lineup_ids { get; set; } = Array.Empty<string>();

    public DateTime expires_at { get; set; }
}

// The whole of the map-change decision, kept out of both plugins so the arg
// parsing and the changelevel branch are the same in each and can be tested
// without a server.
public static class PracticeMapChangeUtility
{
    // A client that has not come back by now is not coming back for THIS
    // reason: they alt-tabbed, or their game crashed on the load. Standing them
    // on a lineup they asked for ten minutes ago would be a teleport out of
    // nowhere.
    public const int PendingSeconds = 90;

    // Long enough to read the line and stop what you are doing, short enough
    // that it is not a wait. The switch is never confirmed on the website, so
    // this countdown is the only warning anybody else on the server gets.
    public const int CountdownSeconds = 5;

    /// usage: utility_practice_map &lt;map|workshop id&gt; [&lt;steamid64&gt; &lt;id[,id,...]&gt;]
    public static bool TryParse(string[] args, out PracticeMapChangeRequest request)
    {
        request = new PracticeMapChangeRequest();

        if (args.Length < 1)
        {
            return false;
        }

        string map = args[0].Trim().Trim('"');

        if (string.IsNullOrEmpty(map))
        {
            return false;
        }

        request.map = map;

        // The load half is optional: a map change asked for from the top bar
        // has nobody to stand anywhere.
        if (args.Length < 3)
        {
            return true;
        }

        if (!ulong.TryParse(args[1].Trim(), out ulong steamId))
        {
            return false;
        }

        string[] ids = args[2]
            .Trim()
            .Trim('"')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (ids.Length == 0)
        {
            return true;
        }

        request.steam_id = steamId;
        request.lineup_ids = ids;

        return true;
    }

    // A workshop map has a numeric id where its name would be, and changelevel
    // cannot load one. Same branch MatchManager.ChangeMap takes.
    public static string Command(string map)
    {
        return IsWorkshopId(map) ? $"host_workshop_map {map}" : $"changelevel \"{map}\"";
    }

    public static bool IsWorkshopId(string map)
    {
        return map.Length > 0 && map.All(char.IsDigit);
    }

    public static PracticeMapChangePending? PendingFor(
        PracticeMapChangeRequest request,
        DateTime now
    )
    {
        if (!request.HasLoad)
        {
            return null;
        }

        return new PracticeMapChangePending
        {
            steam_id = request.steam_id,
            lineup_ids = request.lineup_ids,
            expires_at = now.AddSeconds(PendingSeconds),
        };
    }

    public static bool IsExpired(PracticeMapChangePending? pending, DateTime now)
    {
        return pending == null || now >= pending.expires_at;
    }
}
