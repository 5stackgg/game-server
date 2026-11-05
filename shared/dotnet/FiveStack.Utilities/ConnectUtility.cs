using System.Security.Cryptography;
using System.Text;
using FiveStack.Enums;

namespace FiveStack.Utilities;

// Who a server is expecting, as its plugin knows it: a match plugin from the
// lineups, a practice plugin from the session roster.
public class ConnectRules
{
    public Guid match_id { get; set; }
    public string password { get; set; } = "";
    public bool is_member { get; set; }

    // The role a client presenting the raw server password joins as.
    public string? password_role { get; set; }

    // Only read by the log.
    public List<string> roster { get; set; } = new List<string>();
    public DateTime? loaded_at { get; set; }
}

public class ConnectDecision
{
    public eConnectAction action { get; set; }
    public string? pending_role { get; set; }
    public string reason { get; set; } = "";
}

// The door policy every ConnectClient hook runs -- both match plugins and both
// practice plugins -- so a connect is decided, and logged, the same everywhere.
public static class ConnectUtility
{
    public static ConnectDecision Authorize(ConnectRules? rules, ulong steamId, string? token)
    {
        // Deny by default: nothing loaded must not read as "everyone is
        // welcome", so the engine's own password check stays the gate.
        if (rules == null)
        {
            return Decide(eConnectAction.PasswordCheck, "no match or session loaded");
        }

        if (token == null)
        {
            return Decide(eConnectAction.Reject, "no password parameter");
        }

        if (!string.IsNullOrEmpty(rules.password) && token == rules.password)
        {
            return Decide(eConnectAction.Authorized, "server password", rules.password_role);
        }

        if (rules.is_member)
        {
            return Decide(eConnectAction.Authorized, "on the roster");
        }

        string[] parts = token.Split(':');

        if (parts.Length != 3)
        {
            return Decide(
                eConnectAction.Reject,
                token.Length == 0
                    ? "not on the roster and sent no password"
                    : "not on the roster and the password is not a connect token"
            );
        }

        string type = parts[0];
        string role = parts[1];

        string expected = ConnectAuth.ComputeExpectedToken(
            rules.password,
            type,
            role,
            steamId,
            rules.match_id
        );

        // Constant-time comparison so verifying the connect token does not leak
        // the correct value through response timing.
        bool matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(ConnectAuth.NormalizeClientToken(parts[2]))
        );

        if (!matches)
        {
            return type == "tv"
                ? Decide(eConnectAction.Reject, "tv token not signed for this player and match")
                : Decide(
                    eConnectAction.PasswordCheck,
                    $"{type} token not signed for this player and match"
                );
        }

        return Decide(eConnectAction.Authorized, $"{type} token", PendingRole(type, role));
    }

    // Never the token itself: it is the server password, or signed with it.
    public static string Describe(
        ConnectRules? rules,
        ConnectDecision decision,
        ulong steamId,
        string? name,
        string? token,
        int ticketLength,
        bool passwordReady
    )
    {
        var line = new StringBuilder();

        line.Append($"connect {steamId} '{name}': {decision.action} -- {decision.reason}");

        if (decision.pending_role != null)
        {
            line.Append($" as {decision.pending_role}");
        }

        line.Append($" | token: {TokenShape(rules, token)}");
        line.Append($" | ticket: {ticketLength} bytes");
        line.Append($" | password ready: {passwordReady}");

        if (rules == null)
        {
            return line.ToString();
        }

        line.Append($" | match: {rules.match_id}");
        line.Append($" | member: {rules.is_member}");
        line.Append($" | roster: {rules.roster.Count}");

        if (rules.loaded_at != null)
        {
            line.Append($", loaded {Age(DateTime.UtcNow - rules.loaded_at.Value)} ago");
        }

        // The ids only when somebody was turned away: on a busy server the
        // accepted connects would repeat the whole lineup every time.
        if (decision.action != eConnectAction.Authorized)
        {
            line.Append($" [{string.Join(", ", rules.roster)}]");
        }

        return line.ToString();
    }

    private static string TokenShape(ConnectRules? rules, string? token)
    {
        if (token == null)
        {
            return "none";
        }

        if (token.Length == 0)
        {
            return "empty";
        }

        if (rules != null && !string.IsNullOrEmpty(rules.password) && token == rules.password)
        {
            return "server password";
        }

        string[] parts = token.Split(':');

        if (parts.Length == 3)
        {
            return $"{parts[0]}:{parts[1]}:<{parts[2].Length} chars>";
        }

        return $"{token.Length} chars, {parts.Length} part(s)";
    }

    private static string Age(TimeSpan age)
    {
        if (age.TotalMinutes < 1)
        {
            return $"{(int)age.TotalSeconds}s";
        }

        if (age.TotalHours < 1)
        {
            return $"{(int)age.TotalMinutes}m";
        }

        return $"{(int)age.TotalHours}h{age.Minutes}m";
    }

    private static ConnectDecision Decide(
        eConnectAction action,
        string reason,
        string? pendingRole = null
    )
    {
        return new ConnectDecision
        {
            action = action,
            reason = reason,
            pending_role = pendingRole,
        };
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
