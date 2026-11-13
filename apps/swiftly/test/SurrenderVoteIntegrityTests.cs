using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SwiftlyS2.Shared.Players;
using FiveStack;
using FiveStack.Entities;
using Xunit;

public class SurrenderVoteIntegrityTests
{
    // A spectator calling surrender opened a vote for spectators that handed
    // the map to CT.
    [Fact]
    public void Nobody_Off_The_Two_Sides_Can_Call_A_Surrender()
    {
        Assert.False(SurrenderSystem.CanCallSurrender(Team.Spectator, false));
        Assert.False(SurrenderSystem.CanCallSurrender(Team.Spectator, true));
        Assert.False(SurrenderSystem.CanCallSurrender(Team.None, true));
    }

    [Fact]
    public void Only_A_Lineup_Player_On_A_Side_Can_Call_One()
    {
        Assert.True(SurrenderSystem.CanCallSurrender(Team.T, true));
        Assert.True(SurrenderSystem.CanCallSurrender(Team.CT, true));
        Assert.False(SurrenderSystem.CanCallSurrender(Team.CT, false));
    }

    private static readonly Guid Lineup1 = Guid.NewGuid();
    private static readonly Guid Lineup2 = Guid.NewGuid();

    [Fact]
    public void The_Lineup_That_Gives_Up_Is_The_One_That_Did_Not_Win()
    {
        var match = new MatchData { lineup_1_id = Lineup1, lineup_2_id = Lineup2 };

        Assert.Equal(Lineup1, SurrenderSystem.SurrenderingLineup(match, Lineup2));
        Assert.Equal(Lineup2, SurrenderSystem.SurrenderingLineup(match, Lineup1));
        Assert.Null(SurrenderSystem.SurrenderingLineup(match, Guid.NewGuid()));
    }

    // Rounds played moves at round end, before players are swapped, so for that
    // window the opponents stand on the surrendering side.
    [Fact]
    public void Opponents_On_The_Surrendering_Side_Before_The_Swap_Do_Not_Vote()
    {
        Assert.False(SurrenderSystem.CanVoteInSurrender(Lineup2, Lineup1));
        Assert.True(SurrenderSystem.CanVoteInSurrender(Lineup1, Lineup1));
    }

    // A halftime inside the 30 seconds swaps the sides but not the lineups.
    [Fact]
    public void A_Halftime_Inside_The_Vote_Does_Not_Change_Who_Votes()
    {
        Assert.True(SurrenderSystem.CanVoteInSurrender(Lineup1, Lineup1));
        Assert.False(SurrenderSystem.CanVoteInSurrender(Lineup2, Lineup1));
    }

    // Coaches, casters and spectators are on no lineup.
    [Fact]
    public void Nobody_Off_The_Lineups_Votes()
    {
        Assert.False(SurrenderSystem.CanVoteInSurrender(null, Lineup1));
    }

    private static string Code(string folder, string file)
    {
        return Regex.Replace(
            File.ReadAllText(Path.Combine(SourceDir(), folder, file)),
            @"//[^\n]*",
            ""
        );
    }

    private static string Method(string source, string signature)
    {
        int from = source.IndexOf(signature, StringComparison.Ordinal);

        Assert.True(from >= 0, $"{signature} is gone");

        int to = source.IndexOf("\n    }\n", from, StringComparison.Ordinal);

        Assert.True(to > from, $"the end of {signature} was not found");

        return source[from..to];
    }

    // Running the vote needs the engine (timers and player objects), so these two
    // read the plugin: the vote has to be started with the lineup's filter, and
    // the vote has to turn away anyone that filter rejects.
    [Fact]
    public void The_Surrender_Vote_Is_Started_With_The_Lineup_Filter()
    {
        string setup = Method(Code("FiveStack.Services", "SurrenderSystem.cs"), "public void SetupSurrender(");
        int start = setup.IndexOf("surrenderingVote.StartVote(", StringComparison.Ordinal);

        Assert.True(start >= 0, "the surrender vote is never started");
        Assert.Matches(
            @"voterFilter:\s*voter\s*=>\s*CanVoteInSurrender\(\s*VoterLineup\(voter\),\s*surrenderingLineup\.Value\s*\)",
            setup[start..]
        );
    }

    [Fact]
    public void A_Voter_The_Filter_Rejects_Never_Counts()
    {
        string canVote = Method(Code("FiveStack.Services", "VoteSystem.cs"), "private bool CanVote(");

        Assert.Matches(
            @"^private bool CanVote\([^)]*\)\s*\{\s*if \(_voterFilter != null && !_voterFilter\(player\)\)\s*\{\s*return false;\s*\}",
            canVote
        );
    }

    private static string SourceDir([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "src"));
    }

    // A failed vote reset the whole surrender system, which killed every other
    // leaver's disconnect timer: no abandon, and no vote when it ran out. The
    // timers need the engine, so this reads the plugin.
    [Fact]
    public void A_Failed_Vote_Leaves_Other_Leavers_Timers_Alone()
    {
        string source = Regex.Replace(
            File.ReadAllText(Path.Combine(SourceDir(), "FiveStack.Services", "SurrenderSystem.cs")),
            @"//[^\n]*",
            ""
        );
        int failed = source.IndexOf("\"surrender vote failed\"", StringComparison.Ordinal);

        Assert.True(failed >= 0, "the failure callback is gone");

        int end = source.IndexOf("},", failed, StringComparison.Ordinal);
        string callback = source[failed..end];

        Assert.DoesNotContain("Reset()", callback);
        Assert.Contains("surrenderingVote = null;", callback);
    }
}
