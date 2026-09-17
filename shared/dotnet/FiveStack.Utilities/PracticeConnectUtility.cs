using FiveStack.Entities.Practice;

namespace FiveStack.Utilities;

// The practice plugin never runs beside the match plugin, so its door is the
// session roster rather than a lineup. The policy itself is ConnectUtility's.
public static class PracticeConnectUtility
{
    public static ConnectRules? Rules(
        PracticeSessionData? session,
        ulong steamId,
        DateTime? loadedAt = null
    )
    {
        if (session == null)
        {
            return null;
        }

        return new ConnectRules
        {
            match_id = session.match_id,
            password = session.password,
            is_member = IsOnRoster(session, steamId),
            roster = session.allowed_steam_ids,
            loaded_at = loadedAt,
        };
    }

    public static bool IsOnRoster(PracticeSessionData session, ulong steamId)
    {
        string id = steamId.ToString();

        return session.allowed_steam_ids.Any(allowed => allowed.Trim() == id);
    }
}
