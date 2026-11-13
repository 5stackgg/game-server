using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SwiftlyS2.Shared.Players;
using FiveStack;
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

    // Coaches and casters sitting on the surrendering side are not its players.
    [Fact]
    public void Only_The_Surrendering_Sides_Lineup_Players_Vote()
    {
        Assert.True(SurrenderSystem.CanVoteInSurrender(Team.CT, true, Team.CT));
        Assert.False(SurrenderSystem.CanVoteInSurrender(Team.CT, false, Team.CT));
        Assert.False(SurrenderSystem.CanVoteInSurrender(Team.T, true, Team.CT));
        Assert.False(SurrenderSystem.CanVoteInSurrender(Team.Spectator, false, Team.CT));
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
