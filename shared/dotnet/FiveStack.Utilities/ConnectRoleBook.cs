namespace FiveStack.Utilities;

// A role is only good for the match its connect token was signed for, and a
// dedicated server loads match after match with players still connected.
public class ConnectRoleBook
{
    private readonly Dictionary<ulong, (Guid MatchId, string Role)> _pending = new();
    private readonly Dictionary<ulong, (Guid MatchId, string Role)> _connected = new();

    public void Offer(ulong steamId, Guid? matchId, string? role)
    {
        if (matchId == null || role == null)
        {
            _pending.Remove(steamId);
            return;
        }

        _pending[steamId] = (matchId.Value, role);
    }

    public string? Claim(ulong steamId, Guid matchId)
    {
        if (!_pending.Remove(steamId, out var pending) || pending.MatchId != matchId)
        {
            return null;
        }

        _connected[steamId] = pending;

        return pending.Role;
    }

    public void Leave(ulong steamId)
    {
        _connected.Remove(steamId);
    }

    public string? RoleIn(ulong steamId, Guid? matchId)
    {
        if (!_connected.TryGetValue(steamId, out var connected) || connected.MatchId != matchId)
        {
            return null;
        }

        return connected.Role;
    }
}
