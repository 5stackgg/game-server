using FiveStack.Entities.PlayerManagement;

namespace FiveStack.Utilities;

public sealed record PlayerManagementPlayer(string Name, string SteamId, SanctionState State);

// What the plugin answers over RCON. The panel reads the refresh reply to tell
// "installed and syncing" from "installed but not configured" from "not
// installed" (an unknown command), so Marker and the syncing line are a
// contract with the api, not just text.
public static class PlayerManagementReport
{
    public const string Marker = "PlayerManagement:";

    public static string Syncing(int players)
    {
        return $"{Marker} syncing {players} player(s)";
    }

    public static string NotConfigured()
    {
        return $"{Marker} not configured; set API_DOMAIN, SERVER_ID and SERVER_API_PASSWORD";
    }

    public static string Status(
        string version,
        string runtime,
        PlayerManagementSettings settings,
        DateTimeOffset? lastSyncAt,
        string? lastError,
        ServerAccessSnapshot access,
        IReadOnlyCollection<PlayerManagementPlayer> players,
        DateTimeOffset now
    )
    {
        List<string> lines =
        [
            "----- 5Stack Player Management -----",
            $"Plugin Version: {version}",
            $"Plugin Runtime: {runtime}",
            $"Server ID: {(string.IsNullOrEmpty(settings.SERVER_ID) ? "unassigned" : settings.SERVER_ID)}",
            $"API: {settings.API_DOMAIN}",
            $"Configured: {(settings.IsConnected() ? "yes" : "no")}",
            $"Last Sync: {LastSync(lastSyncAt, lastError, now)}",
            $"Access: {Access(access)}",
            $"Players: {players.Count}",
        ];

        foreach (PlayerManagementPlayer player in players)
        {
            lines.Add($"  {player.Name} ({player.SteamId}): {Describe(player.State)}");
        }

        return string.Join("\n", lines);
    }

    public static string Describe(SanctionState state)
    {
        List<string> parts = new();

        if (state.IsBanned)
        {
            parts.Add("banned");
        }

        if (state.IsMuted)
        {
            parts.Add("muted");
        }

        if (state.IsGagged)
        {
            parts.Add("gagged");
        }

        return parts.Count == 0 ? "clean" : string.Join(", ", parts);
    }

    public static string Access(ServerAccessSnapshot access)
    {
        string state = access.Loaded
            ? $"loaded yes, restricted {(access.Restricted ? "yes" : "no")}, version {access.Version}, allowlist {access.Allowed}"
            : "loaded no, restricted unknown, version none, allowlist 0";

        return access.Error == null ? state : $"{state}; last fetch failed ({access.Error})";
    }

    private static string LastSync(DateTimeOffset? at, string? error, DateTimeOffset now)
    {
        string when =
            at == null ? "never" : $"{Math.Max(0, (int)(now - at.Value).TotalSeconds)}s ago";

        return error == null ? when : $"failed ({error}); last success {when}";
    }
}
