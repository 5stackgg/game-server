using FiveStack.Utilities;
using Xunit;

public class PingReplyUtilityTests
{
    [Fact]
    public void AMatchIsFetchedOnlyWhenThePanelAsksForIt()
    {
        Assert.True(PingReplyUtility.WantsMatch("""{"get_match":true}"""));
        Assert.False(PingReplyUtility.WantsMatch("""{"get_match":false}"""));
    }

    // Panels older than this reply with nothing at all.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("OK")]
    [InlineData("""{"get_match":"true"}""")]
    public void AnythingElseIsNotARequest(string? body)
    {
        Assert.False(PingReplyUtility.WantsMatch(body));
    }
}
