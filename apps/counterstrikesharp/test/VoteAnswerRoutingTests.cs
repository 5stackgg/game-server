using System.Runtime.CompilerServices;
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

    // The vote turns the answer away itself, and a captain-only vote tells a
    // non-captain they are not allowed to vote instead of saying nothing.
    [Fact]
    public void A_Named_Vote_The_Player_Cannot_Answer_Still_Gets_It_To_Turn_Away()
    {
        var (targets, _) = VoteAnswerRouting.Route(
            new[] { ("surrender", false), ("resume", true) },
            "surrender"
        );

        Assert.Equal(new[] { "surrender" }, targets);
    }

    [Fact]
    public void The_Only_Open_Vote_Gets_The_Answer_Even_If_It_Will_Turn_It_Away()
    {
        var (targets, choices) = VoteAnswerRouting.Route(new[] { ("pause", false) }, null);

        Assert.Equal(new[] { "pause" }, targets);
        Assert.Empty(choices);
    }

    [Fact]
    public void A_Vote_That_Is_Not_Open_Cannot_Be_Named()
    {
        var (targets, _) = VoteAnswerRouting.Route(new[] { ("resume", true) }, "surrender");

        Assert.Empty(targets);
    }

    private static string AppDir([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, ".."));
    }

    [Fact]
    public void The_Pick_One_Message_Is_Translated_In_Every_Language()
    {
        string[] locales = Directory.GetFiles(Path.Combine(AppDir(), "src", "lang"), "*.json");

        Assert.Equal(17, locales.Length);
        foreach (string locale in locales)
        {
            Assert.Contains("\"vote.choose_one\"", File.ReadAllText(locale));
        }

        string command = File.ReadAllText(Path.Combine(AppDir(), "src", "FiveStack.Commands", "Vote.cs"));

        Assert.Contains("\"vote.choose_one\"", command);
        Assert.DoesNotContain("More than one vote is open", command);
    }
}
