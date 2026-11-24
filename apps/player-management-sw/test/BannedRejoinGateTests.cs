using FiveStack.Entities.PlayerManagement;
using FiveStack.Utilities;
using Xunit;

public class BannedRejoinGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private static SanctionBook Banned(string steamId, DateTimeOffset? expiresAt = null)
    {
        SanctionBook book = new();
        book.Record(
            [steamId],
            [
                new PlayerSanction
                {
                    steam_id = steamId,
                    type = "ban",
                    expires_at = expiresAt,
                },
            ]
        );
        return book;
    }

    [Fact]
    public void RefusesAPlayerKickedForABanWhileTheBanHolds()
    {
        SanctionBook book = Banned("1");
        BannedRejoinGate gate = new();
        gate.Kicked(1);

        Assert.True(gate.Refuse(1, book, Now));
        Assert.True(gate.Refuse(1, book, Now));
    }

    // Asking the panel again is how a lifted ban gets back in.
    [Fact]
    public void ARefusalAsksThePanelAboutThePlayer()
    {
        SanctionBook book = Banned("1");
        BannedRejoinGate gate = new();
        gate.Kicked(1);

        gate.Refuse(1, book, Now);

        Assert.True(book.IsAwaiting("1"));
    }

    [Fact]
    public void LetsInAPlayerThisServerNeverKicked()
    {
        SanctionBook book = Banned("1");
        BannedRejoinGate gate = new();

        Assert.False(gate.Refuse(1, book, Now));
        Assert.False(book.IsAwaiting("1"));
    }

    [Fact]
    public void LetsInAPlayerWhoseBanWasLiftedAndForgetsTheKick()
    {
        SanctionBook book = Banned("1");
        BannedRejoinGate gate = new();
        gate.Kicked(1);

        book.Record(["1"], []);

        Assert.False(gate.Refuse(1, book, Now));

        book.Record(["1"], [new PlayerSanction { steam_id = "1", type = "ban" }]);

        Assert.False(gate.Refuse(1, book, Now));
    }

    [Fact]
    public void LetsInAPlayerWhoseBanRanOut()
    {
        SanctionBook book = Banned("1", Now.AddMinutes(-1));
        BannedRejoinGate gate = new();
        gate.Kicked(1);

        Assert.False(gate.Refuse(1, book, Now));
    }

    [Fact]
    public void LetsInAConnectWithNoSteamId()
    {
        SanctionBook book = Banned("0");
        BannedRejoinGate gate = new();
        gate.Kicked(0);

        Assert.False(gate.Refuse(0, book, Now));
    }
}
