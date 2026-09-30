using FiveStack.Entities.PlayerManagement;
using FiveStack.Utilities;
using Xunit;

public class PlayerManagementReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    // The api's sanctionServerPlayer reads these two lines back over RCON to
    // decide what to tell the moderator; changing them breaks that check.
    [Fact]
    public void TheRefreshRepliesKeepTheirContractWithTheApi()
    {
        Assert.Equal("PlayerManagement: syncing 3 player(s)", PlayerManagementReport.Syncing(3));
        Assert.StartsWith(
            "PlayerManagement: not configured",
            PlayerManagementReport.NotConfigured()
        );
    }

    [Fact]
    public void TheStatusListsEachPlayersSanctions()
    {
        string report = PlayerManagementReport.Status(
            "0.0.9",
            "swiftlys2",
            new PlayerManagementSettings
            {
                API_DOMAIN = "https://api.example.com",
                SERVER_ID = "11111111-1111-1111-1111-111111111111",
                SERVER_API_PASSWORD = "secret",
            },
            Now.AddSeconds(-12),
            null,
            [
                new PlayerManagementPlayer("clean", "1", SanctionState.None),
                new PlayerManagementPlayer(
                    "noisy",
                    "2",
                    new SanctionState(
                        null,
                        new PlayerSanction { type = "silence" },
                        new PlayerSanction { type = "silence" }
                    )
                ),
            ],
            Now
        );

        Assert.Contains("Plugin Version: 0.0.9", report);
        Assert.Contains("Configured: yes", report);
        Assert.Contains("Last Sync: 12s ago", report);
        Assert.Contains("Players: 2", report);
        Assert.Contains("  clean (1): clean", report);
        Assert.Contains("  noisy (2): muted, gagged", report);
        Assert.DoesNotContain("secret", report);
    }

    [Fact]
    public void AFailingSyncSaysWhyAndWhenItLastWorked()
    {
        string report = PlayerManagementReport.Status(
            "0.0.9",
            "counterstrikesharp",
            new PlayerManagementSettings(),
            null,
            "401 unauthorized",
            [],
            Now
        );

        Assert.Contains("Configured: no", report);
        Assert.Contains("Last Sync: failed (401 unauthorized); last success never", report);
    }
}
