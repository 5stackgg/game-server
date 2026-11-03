namespace FiveStack.Utilities;

// A card has a handful of rows and a server can hold a dozen bots. Past the
// rows there are, everybody one grenade caught is counted by what it did to
// them rather than listed by name.
public static class PracticeCardGroups
{
    // Judged on the same seconds the card shows, or a row reading 3.0s sits
    // above a foot saying nobody was blind for one.
    public const float WorkedSeconds = 1f;

    public record FlashHit(float Seconds, float White, bool Target, bool Bad);

    public record Group(string Label, int Count, float Seconds, float White, bool Bad);

    public static int Worked(IReadOnlyList<FlashHit> hits)
    {
        return hits.Count(hit => hit.Target && hit.Seconds >= WorkedSeconds);
    }

    public static IReadOnlyList<Group> Flash(IReadOnlyList<FlashHit> hits)
    {
        var groups = new List<Group>();

        void Add(string label, IEnumerable<FlashHit> members, bool bad)
        {
            List<FlashHit> list = members.ToList();

            if (list.Count > 0)
            {
                groups.Add(
                    new Group(label, list.Count, list.Max(hit => hit.Seconds), list.Max(hit => hit.White), bad)
                );
            }
        }

        Add("blinded", hits.Where(hit => hit.Target && hit.Seconds >= WorkedSeconds), false);
        Add("dazzled", hits.Where(hit => hit.Target && hit.Seconds < WorkedSeconds), false);
        Add("your side", hits.Where(hit => hit.Bad), true);

        return groups;
    }

    public record Burn(bool Target, bool Bad);

    public static IReadOnlyList<Group> Fire(IReadOnlyList<Burn> burns)
    {
        var groups = new List<Group>();
        int targets = burns.Count(burn => burn.Target);
        int own = burns.Count(burn => burn.Bad);

        if (targets > 0)
        {
            groups.Add(new Group("burned", targets, 0f, 0f, false));
        }

        if (own > 0)
        {
            groups.Add(new Group("your side", own, 0f, 0f, true));
        }

        return groups;
    }
}
