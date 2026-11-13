using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack;
using Xunit;

public class SurrenderVoteIntegrityTests
{
    // A spectator calling surrender opened a vote for spectators that handed
    // the map to CT.
    [Fact]
    public void Nobody_Off_The_Two_Sides_Can_Call_A_Surrender()
    {
        Assert.False(SurrenderSystem.CanCallSurrender(CsTeam.Spectator, false));
        Assert.False(SurrenderSystem.CanCallSurrender(CsTeam.Spectator, true));
        Assert.False(SurrenderSystem.CanCallSurrender(CsTeam.None, true));
    }

    [Fact]
    public void Only_A_Lineup_Player_On_A_Side_Can_Call_One()
    {
        Assert.True(SurrenderSystem.CanCallSurrender(CsTeam.Terrorist, true));
        Assert.True(SurrenderSystem.CanCallSurrender(CsTeam.CounterTerrorist, true));
        Assert.False(SurrenderSystem.CanCallSurrender(CsTeam.CounterTerrorist, false));
    }

    // Coaches and casters sitting on the surrendering side are not its players.
    [Fact]
    public void Only_The_Surrendering_Sides_Lineup_Players_Vote()
    {
        Assert.True(SurrenderSystem.CanVoteInSurrender(CsTeam.CounterTerrorist, true, CsTeam.CounterTerrorist));
        Assert.False(SurrenderSystem.CanVoteInSurrender(CsTeam.CounterTerrorist, false, CsTeam.CounterTerrorist));
        Assert.False(SurrenderSystem.CanVoteInSurrender(CsTeam.Terrorist, true, CsTeam.CounterTerrorist));
        Assert.False(SurrenderSystem.CanVoteInSurrender(CsTeam.Spectator, false, CsTeam.CounterTerrorist));
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
