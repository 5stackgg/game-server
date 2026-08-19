using System.Security.Cryptography;
using System.Text;
using FiveStack.Entities.Practice;
using FiveStack.Enums;

namespace FiveStack.Utilities;

public class PracticeConnectDecision
{
    public ePracticeConnect action { get; set; }
    public string? pending_role { get; set; }
}

// The practice plugin never runs beside the match plugin, so it carries its own
// door policy. This is the whole of it, as a pure function of the cached
// session and the client's token, so both runtimes' hooks stay thin.
public static class PracticeConnectUtility
{
    public static PracticeConnectDecision Authorize(
        PracticeSessionData? session,
        ulong steamId,
        string? token
    )
    {
        // Deny by default: an unloaded roster must not read as "everyone is
        // welcome", so the engine's own password check stays the gate.
        if (session == null)
        {
            return new PracticeConnectDecision { action = ePracticeConnect.PasswordCheck };
        }

        if (token == null)
        {
            return new PracticeConnectDecision { action = ePracticeConnect.Reject };
        }

        if (!string.IsNullOrEmpty(session.password) && token == session.password)
        {
            return new PracticeConnectDecision { action = ePracticeConnect.Authorized };
        }

        if (IsOnRoster(session, steamId))
        {
            return new PracticeConnectDecision { action = ePracticeConnect.Authorized };
        }

        string[] parts = token.Split(':');

        if (parts.Length != 3)
        {
            return new PracticeConnectDecision { action = ePracticeConnect.Reject };
        }

        string type = parts[0];
        string role = parts[1];

        string expected = ConnectAuth.ComputeExpectedToken(
            session.password,
            type,
            role,
            steamId,
            session.match_id
        );

        // Constant-time comparison so verifying the connect token does not leak
        // the correct value through response timing.
        bool matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(ConnectAuth.NormalizeClientToken(parts[2]))
        );

        if (!matches)
        {
            return new PracticeConnectDecision
            {
                action = type == "tv" ? ePracticeConnect.Reject : ePracticeConnect.PasswordCheck,
            };
        }

        return new PracticeConnectDecision
        {
            action = ePracticeConnect.Authorized,
            pending_role = PendingRole(type, role),
        };
    }

    public static bool IsOnRoster(PracticeSessionData session, ulong steamId)
    {
        string id = steamId.ToString();

        return session.allowed_steam_ids.Any(allowed => allowed.Trim() == id);
    }

    private static string? PendingRole(string type, string role)
    {
        if (type != "game")
        {
            return null;
        }

        ePlayerRoles playerRole = PlayerRoleUtility.PlayerRoleStringToEnum(role);

        if (playerRole == ePlayerRoles.Administrator)
        {
            return "admin";
        }

        if (playerRole == ePlayerRoles.Streamer)
        {
            return "streamer";
        }

        if (
            playerRole == ePlayerRoles.MatchOrganizer
            || playerRole == ePlayerRoles.TournamentOrganizer
        )
        {
            return "organizer";
        }

        return null;
    }
}
