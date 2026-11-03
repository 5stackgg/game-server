using FiveStack.Entities.Practice;

namespace FiveStack.Utilities;

public static class PracticeSaveUtility
{
    // Whatever the record's id says: a throw saved twice can have one save with
    // an id and another still held. Returns the id left to delete from the panel.
    public static string? ForgetSaves(
        LineupRecord lineup,
        Action<LineupRecord> release,
        Action<string> withdraw
    )
    {
        release(lineup);
        withdraw(lineup.client_id);

        return GiveUpPanelId(lineup);
    }

    // A panel row's client id is its panel id and a session save keeps its own,
    // so after a push the same lineup can be either. Holds the id as it is now,
    // because ForgetSaves gives it up.
    public static Predicate<LineupRecord> SameLineup(LineupRecord lineup)
    {
        string clientId = lineup.client_id;
        string? id = lineup.id;

        return row =>
            row.client_id == clientId || (PracticeLineupUtility.IsPanelId(id) && row.id == id);
    }

    // Where the walk stands once the removed rows are gone: on the same lineup,
    // or back at the start if that was one of them.
    public static int PruneResults(
        List<LineupRecord> results,
        int index,
        Predicate<LineupRecord> gone
    )
    {
        LineupRecord? standing = index >= 0 && index < results.Count ? results[index] : null;

        results.RemoveAll(gone);

        return standing == null ? -1 : results.IndexOf(standing);
    }

    // A walk holds rows from before a push, and one that was edited would stand a
    // player on its old spot. Its place in the walk stays: this only swaps rows.
    public static void RefreshResults(
        List<LineupRecord> results,
        IEnumerable<LineupRecord> library
    )
    {
        for (int step = 0; step < results.Count; step++)
        {
            string? id = results[step].id;
            LineupRecord? fresh = PracticeLineupUtility.IsPanelId(id)
                ? library.FirstOrDefault(row => row.id == id)
                : null;

            if (fresh != null)
            {
                results[step] = fresh;
            }
        }
    }

    public static void GivePanelId(LineupRecord saved, string? id)
    {
        saved.id = id ?? saved.id;
    }

    // The record outlives a delete as the player's last throw, and a later save
    // of that throw must not inherit the id of the row just deleted.
    public static string? GiveUpPanelId(LineupRecord lineup)
    {
        string? id = lineup.id;
        lineup.id = null;

        return id;
    }

    // A library refresh while a save was held replaces the list it was in. The
    // refresh may also have brought the panel's own row for it, which carries a
    // different client id, so matching on the panel id is what stops a double.
    public static bool BelongsBackInLibrary(
        LineupRecord saved,
        string map,
        IEnumerable<LineupRecord> library
    )
    {
        return saved.map == map
            && !library.Any(row =>
                ReferenceEquals(row, saved) || (saved.id != null && row.id == saved.id)
            );
    }
}
