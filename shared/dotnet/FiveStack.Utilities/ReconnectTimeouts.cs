namespace FiveStack.Utilities;

public class ReconnectTimeouts
{
    private readonly Dictionary<Guid, int> _used = new Dictionary<Guid, int>();
    private readonly List<Guid> _lastCharged = new List<Guid>();
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

        _lastCharged.Clear();

        foreach (Guid lineup in shortLineups.Distinct())
        {
            int used = _used.GetValueOrDefault(lineup);

            if (used >= perLineup)
            {
                continue;
            }

            _used[lineup] = used + 1;
            _lastCharged.Add(lineup);
        }

        return _lastCharged.Count > 0;
    }

    // A timeout whose pause never happened is given back.
    public void Refund()
    {
        foreach (Guid lineup in _lastCharged)
        {
            _used[lineup] = Math.Max(0, _used.GetValueOrDefault(lineup) - 1);
        }

        _lastCharged.Clear();
    }
}
