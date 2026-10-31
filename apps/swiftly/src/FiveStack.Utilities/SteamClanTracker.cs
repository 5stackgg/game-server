namespace FiveStack.Utilities;

// The Steam group each player picked as their clan tag in CS2. The SetClan hook
// keeps the game from showing it, so this is the only place it is still known.
// Controllers are reused per slot, so a leaving player's entry must be forgotten.
public class SteamClanTracker
{
    private readonly Dictionary<nint, uint> _clanIds = new();

    public bool Record(nint controller, uint clanId)
    {
        if (_clanIds.TryGetValue(controller, out uint known) && known == clanId)
        {
            return false;
        }

        _clanIds[controller] = clanId;
        return true;
    }

    public uint? Get(nint controller)
    {
        return _clanIds.TryGetValue(controller, out uint clanId) ? clanId : null;
    }

    public void Forget(nint controller)
    {
        _clanIds.Remove(controller);
    }

    // A map change rebuilds every controller without a disconnect, so an old
    // address can come back holding someone else.
    public void Clear()
    {
        _clanIds.Clear();
    }
}
