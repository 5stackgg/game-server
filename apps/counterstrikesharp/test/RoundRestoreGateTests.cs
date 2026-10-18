using FiveStack.Utilities;
using Xunit;

public class RoundRestoreGateTests
{
    [Fact]
    public void LetsARestoreThroughWithEveryoneIn()
    {
        Assert.Null(RoundRestoreGateUtility.Refusal(false, false, 10, 10, "Competitive"));
    }

    [Fact]
    public void RefusesWhilePlayersAreMissing()
    {
        Assert.Equal(
            "waiting for all players to reconnect (3/10)",
            RoundRestoreGateUtility.Refusal(false, false, 3, 10, "Competitive")
        );
    }

    [Fact]
    public void RefusesWhileAnotherRestoreIsRunning()
    {
        Assert.Equal(
            "a round restore is already in progress",
            RoundRestoreGateUtility.Refusal(false, true, 10, 10, "Competitive")
        );
    }

    [Fact]
    public void RefusesWhereRestoresAreDisabled()
    {
        Assert.Equal(
            "round restores are disabled in Rush",
            RoundRestoreGateUtility.Refusal(true, false, 6, 6, "Rush")
        );
    }
}
