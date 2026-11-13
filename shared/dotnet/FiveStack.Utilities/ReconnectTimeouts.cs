namespace FiveStack.Utilities;

public class ReconnectTimeouts
{
    private readonly Dictionary<Guid, int> _used = new Dictionary<Guid, int>();
    private Guid? _mapId;
    private Guid? _charged;
    private Guid? _paidLast;
    private Guid? _paidBefore;

    public int Used(Guid lineup)
    {
        return _used.GetValueOrDefault(lineup);
    }

    // One short lineup pays for each timeout: the one that has spent fewest,
    // and with those level the one that did not pay last, so two short lineups
    // take turns. Sides swap at halftime, so the count is kept by lineup, and
    // it starts over on a new map.
    public bool Charge(Guid? mapId, IEnumerable<Guid> shortLineups, int perLineup)
    {
        if (_mapId != mapId)
        {
            _mapId = mapId;
            _used.Clear();
            _paidLast = null;
        }

        _charged = null;

        List<Guid> able = shortLineups.Distinct().Where(lineup => Used(lineup) < perLineup).ToList();

        if (able.Count == 0)
        {
            return false;
        }

        Guid payer = able.OrderBy(Used).ThenBy(lineup => lineup == _paidLast).First();

        _used[payer] = Used(payer) + 1;
        _charged = payer;
        _paidBefore = _paidLast;
        _paidLast = payer;

        return true;
    }

    // A timeout whose pause never happened is given back, and with it the turn.
    public void Refund()
    {
        if (_charged is not Guid lineup)
        {
            return;
        }

        _used[lineup] = Math.Max(0, Used(lineup) - 1);
        _paidLast = _paidBefore;
        _charged = null;
    }
}
