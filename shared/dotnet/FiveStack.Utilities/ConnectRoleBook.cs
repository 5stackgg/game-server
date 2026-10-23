using FiveStack.Enums;

namespace FiveStack.Utilities;

// A role is only good for the match its connect token was signed for, and a
// dedicated server loads match after match with players still connected.
public class ConnectRoleBook
{
    private readonly Dictionary<ulong, (Guid MatchId, ePlayerRoles Role, string Tag)> _pending =
        new();
    private readonly Dictionary<ulong, (Guid MatchId, ePlayerRoles Role)> _connected = new();

    public void Offer(ulong steamId, Guid? matchId, string? pendingRole)
    {
        if (matchId == null || pendingRole == null || Parse(pendingRole) is not ePlayerRoles role)
        {
            _pending.Remove(steamId);
            return;
        }

        _pending[steamId] = (matchId.Value, role, pendingRole);
    }

    public string? Claim(ulong steamId, Guid matchId)
    {
        if (!_pending.Remove(steamId, out var pending) || pending.MatchId != matchId)
        {
            return null;
        }

        _connected[steamId] = (pending.MatchId, pending.Role);

        return pending.Tag;
    }

    public void Leave(ulong steamId)
    {
        _connected.Remove(steamId);
    }

    public ePlayerRoles? RoleIn(ulong steamId, Guid? matchId)
    {
        if (!_connected.TryGetValue(steamId, out var connected) || connected.MatchId != matchId)
        {
            return null;
        }

        return connected.Role;
    }

    private static ePlayerRoles? Parse(string pendingRole)
    {
        switch (pendingRole)
        {
            case "admin":
                return ePlayerRoles.Administrator;
            case "organizer":
                return ePlayerRoles.MatchOrganizer;
            case "streamer":
                return ePlayerRoles.Streamer;
            default:
                return null;
        }
    }
}
