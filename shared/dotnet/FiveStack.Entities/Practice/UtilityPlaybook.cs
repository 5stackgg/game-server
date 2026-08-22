namespace FiveStack.Entities.Practice;

// An execute, as GET /utility/session returns it when one is loaded. Steps arrive
// ordered by step_order.
public class UtilityPlaybook
{
    public string? id { get; set; }
    public string? name { get; set; }
    public string? map_name { get; set; }
    public string? side { get; set; }

    public List<UtilityPlaybookStep> steps { get; set; } = new List<UtilityPlaybookStep>();
}

// One throw of an execute. A step with no assigned_steam_id belongs to whoever
// is standing there, which is why it prompts everyone rather than nobody.
public class UtilityPlaybookStep
{
    public string? utility_lineup_id { get; set; }
    public int step_order { get; set; }
    public int offset_ms { get; set; }
    public string? assigned_steam_id { get; set; }
    public string? note { get; set; }

    public UtilityLibraryRow? lineup { get; set; }

    // The step's own id is the authority: a book can name a lineup whose row
    // the panel declined to inline, and loading the wrong geometry is worse
    // than loading none.
    public LineupRecord? ToLineup()
    {
        if (lineup == null)
        {
            return null;
        }

        LineupRecord record = lineup.ToLineup();

        if (!string.IsNullOrEmpty(utility_lineup_id))
        {
            record.id = utility_lineup_id;
            record.client_id = utility_lineup_id;
        }

        return record;
    }
}
