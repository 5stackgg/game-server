using FiveStack.Utilities;
using Xunit;

// A team typing .y to resume a pause also voted yes on its own surrender,
// because every answer went into every open vote.
public class VoteAnswerRoutingTests
{
    [Fact]
    public void One_Answerable_Vote_Gets_The_Answer()
    {
        var (targets, choices) = VoteAnswerRouting.Route(new[] { ("resume", true) }, null);

        Assert.Equal(new[] { "resume" }, targets);
        Assert.Empty(choices);
    }

    [Fact]
    public void With_Two_Open_The_Answer_Counts_For_Neither_And_The_Player_Is_Asked()
    {
        var (targets, choices) = VoteAnswerRouting.Route(
            new[] { ("surrender", true), ("resume", true) },
            null
        );

        Assert.Empty(targets);
        Assert.Equal(new[] { "surrender", "resume" }, choices);
    }

    [Fact]
    public void A_Named_Vote_Gets_The_Answer_Alone()
    {
        var (targets, choices) = VoteAnswerRouting.Route(
            new[] { ("surrender", true), ("resume", true) },
            "Resume"
        );

        Assert.Equal(new[] { "resume" }, targets);
        Assert.Empty(choices);
    }

    [Fact]
    public void A_Vote_The_Player_Cannot_Answer_Is_Left_Out()
    {
        var (targets, _) = VoteAnswerRouting.Route(
            new[] { ("surrender", false), ("resume", true) },
            null
        );

        Assert.Equal(new[] { "resume" }, targets);
    }

    [Fact]
    public void Naming_A_Vote_The_Player_Cannot_Answer_Counts_For_None()
    {
        var (targets, _) = VoteAnswerRouting.Route(
            new[] { ("surrender", false), ("resume", true) },
            "surrender"
        );

        Assert.Empty(targets);
    }
}
