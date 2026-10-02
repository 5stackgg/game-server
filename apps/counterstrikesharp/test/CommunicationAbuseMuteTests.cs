using FiveStack.Utilities;
using Xunit;

public class CommunicationAbuseMuteTests
{
    private const ulong Penalised = 76561198000000001;
    private const ulong Other = 76561198000000002;

    [Fact]
    public void ValveMuteIsClearedForAPlayerWeLeftUnmuted()
    {
        Assert.True(CommunicationAbuseMute.ShouldClear(true, false));
    }

    [Fact]
    public void APlayerWeMutedIsLeftAlone()
    {
        Assert.False(CommunicationAbuseMute.ShouldClear(true, true));
        Assert.False(CommunicationAbuseMute.ShouldClear(false, true));
    }

    [Fact]
    public void NothingToClearWithoutAValveMute()
    {
        Assert.False(CommunicationAbuseMute.ShouldClear(false, false));
    }

    [Fact]
    public void AWatchedPlayerIsRecheckedForTheWholeWindowThenDropped()
    {
        CommunicationAbuseMute recheck = new();
        recheck.Watch(Penalised);

        for (int check = 0; check < CommunicationAbuseMute.RecheckCount; check++)
        {
            Assert.Contains(Penalised, recheck.Due());
        }

        Assert.Empty(recheck.Due());
    }

    [Fact]
    public void WatchingAgainRestartsTheWindow()
    {
        CommunicationAbuseMute recheck = new();
        recheck.Watch(Penalised);

        for (int check = 0; check < CommunicationAbuseMute.RecheckCount - 1; check++)
        {
            recheck.Due();
        }

        recheck.Watch(Penalised);

        for (int check = 0; check < CommunicationAbuseMute.RecheckCount; check++)
        {
            Assert.Contains(Penalised, recheck.Due());
        }

        Assert.Empty(recheck.Due());
    }

    [Fact]
    public void EachPlayerHasTheirOwnWindow()
    {
        CommunicationAbuseMute recheck = new();
        recheck.Watch(Penalised);
        recheck.Due();
        recheck.Watch(Other);

        for (int check = 1; check < CommunicationAbuseMute.RecheckCount; check++)
        {
            recheck.Due();
        }

        Assert.Equal([Other], recheck.Due());
        Assert.Empty(recheck.Due());
    }

    [Fact]
    public void NothingIsDueBeforeAnyoneIsWatched()
    {
        Assert.Empty(new CommunicationAbuseMute().Due());
    }
}
