using FiveStack.Utilities;
using Xunit;

// A get_match answered after a newer one would hand a bound seat back to its
// placeholder and send the player to spectator.
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
}
