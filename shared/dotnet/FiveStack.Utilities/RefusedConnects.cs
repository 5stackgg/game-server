namespace FiveStack.Utilities;

// Clients a connect hook refused while the server has no password: let in, then
// kicked with a reason at connect full. Every connect passes the hook first, so
// a later connect clears the mark; a new map clears them all, since players
// already on the server fire connect full again without passing the hook.
public class RefusedConnects
{
    private readonly HashSet<ulong> _refused = new();

    public void Record(ulong steamId, bool refused)
    {
        if (steamId == 0)
        {
            return;
        }

        if (refused)
        {
            _refused.Add(steamId);
        }
        else
        {
            _refused.Remove(steamId);
        }
    }

    public bool Take(ulong steamId)
    {
        return _refused.Remove(steamId);
    }

    public void Clear()
    {
        _refused.Clear();
    }
}
