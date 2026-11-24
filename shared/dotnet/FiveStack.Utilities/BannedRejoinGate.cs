namespace FiveStack.Utilities;

// A player a server kicked for a ban is refused at connect while the book
// still holds the ban, instead of loading in to be kicked again. Kept after
// they leave, unlike a plugin's kicked list. A refusal marks them awaiting so
// the panel is asked about them, and a lifted ban lets the next try in.
public sealed class BannedRejoinGate
{
    private readonly object _lock = new();
    private readonly HashSet<ulong> _kickedForBan = new();

    public void Kicked(ulong steamId)
    {
        lock (_lock)
        {
            _kickedForBan.Add(steamId);
        }
    }

    public bool Refuse(ulong steamId, SanctionBook book, DateTimeOffset now)
    {
        if (steamId == 0)
        {
            return false;
        }

        lock (_lock)
        {
            if (!_kickedForBan.Contains(steamId))
            {
                return false;
            }

            if (!book.StateFor(steamId.ToString(), now).IsBanned)
            {
                _kickedForBan.Remove(steamId);
                return false;
            }
        }

        book.Joined(steamId.ToString());

        return true;
    }
}
