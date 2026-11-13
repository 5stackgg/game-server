using FiveStack.Utilities;
using Xunit;

// A lineup refresh answered after a newer one would hand a bound seat back to
// its placeholder and send the player to spectator.
public class FetchSequenceTests
{
    [Fact]
    public void An_Older_Answer_After_A_Newer_One_Is_Dropped()
    {
        FetchSequence fetches = new();
        int first = fetches.Request();
        int second = fetches.Request();

        Assert.True(fetches.TryTake(second));
        Assert.False(fetches.TryTake(first));
    }

    [Fact]
    public void Answers_In_Order_Are_All_Taken()
    {
        FetchSequence fetches = new();
        int first = fetches.Request();
        int second = fetches.Request();

        Assert.True(fetches.TryTake(first));
        Assert.True(fetches.TryTake(second));
    }

    [Fact]
    public void One_Refresh_Runs_At_A_Time_And_Asks_Meanwhile_Make_One_More()
    {
        FetchSequence fetches = new();

        Assert.True(fetches.TryStart());
        Assert.False(fetches.TryStart());
        Assert.False(fetches.TryStart());
        Assert.True(fetches.Finish());

        Assert.True(fetches.TryStart());
        Assert.False(fetches.Finish());
    }
}
