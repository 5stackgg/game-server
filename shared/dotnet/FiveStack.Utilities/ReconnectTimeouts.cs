namespace FiveStack.Utilities;

public class ReconnectTimeouts
{
    private readonly Dictionary<Guid, int> _used = new Dictionary<Guid, int>();
    private Guid? _mapId;

    // One timeout holds the match for every lineup that is short, so each of
    // them that still has one pays for it. Sides swap at halftime, so the count
    // is kept by lineup, and it starts over on a new map.
    public bool Charge(Guid? mapId, IEnumerable<Guid> shortLineups, int perLineup)
    {
        if (_mapId != mapId)
        {
            _mapId = mapId;
            _used.Clear();
        }

        bool charged = false;

        foreach (Guid lineup in shortLineups.Distinct())
        {
            int used = _used.GetValueOrDefault(lineup);

            if (used >= perLineup)
            {
                continue;
            }

            _used[lineup] = used + 1;
            charged = true;
        }

        return charged;
    }
}
