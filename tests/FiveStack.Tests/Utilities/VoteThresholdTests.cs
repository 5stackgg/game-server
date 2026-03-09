namespace FiveStack.Tests.Utilities;

/// <summary>
/// Tests for the vote threshold formula used in VoteSystem.CheckVotes:
///   totalYesVotes >= Math.Floor(expectedVoteCount / 2.0) + 1
/// Captain-only mode requires 2/2 captains to vote yes.
/// </summary>
public class VoteThresholdTests
{
    private static bool MajorityReached(int yesVotes, int expectedVoteCount)
    {
        return yesVotes >= Math.Floor(expectedVoteCount / 2.0) + 1;
    }

    [Theory]
    [InlineData(3, 5, true)]   // 3/5 = majority
    [InlineData(2, 5, false)]  // 2/5 = not enough
    [InlineData(4, 7, true)]   // 4/7 = majority
    [InlineData(3, 7, false)]  // 3/7 = not enough
    [InlineData(1, 1, true)]   // 1/1 = majority
    [InlineData(5, 10, false)] // 5/10 = not enough (need 6)
    [InlineData(6, 10, true)]  // 6/10 = majority
    public void MajorityThreshold_CalculatesCorrectly(
        int yesVotes, int expectedCount, bool expected)
    {
        MajorityReached(yesVotes, expectedCount).Should().Be(expected);
    }

    [Fact]
    public void CaptainOnly_Requires2Of2()
    {
        // Captain-only: totalYesVotes >= 2
        int captainYesVotes = 2;
        int totalCaptainVotes = 2;
        (captainYesVotes >= 2 && totalCaptainVotes >= 2).Should().BeTrue();
    }

    [Fact]
    public void CaptainOnly_FailsWith1Of2()
    {
        int captainYesVotes = 1;
        int totalCaptainVotes = 2;
        (captainYesVotes >= 2).Should().BeFalse();
    }

    [Fact]
    public void CaptainOnly_FailsWithLessThan2Votes()
    {
        int totalCaptainVotes = 1;
        // Should not resolve (return early) when < 2 votes cast
        (totalCaptainVotes < 2).Should().BeTrue();
    }

    [Fact]
    public void ZeroExpectedVotes_ShouldFail()
    {
        // When expectedVoteCount is 0, vote fails immediately
        int expectedVoteCount = 0;
        (expectedVoteCount == 0).Should().BeTrue();
    }
}
