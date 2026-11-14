using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// What a leaver costs depends on the match they left: the disconnect timer, the
// surrender vote and the abandon all ask here, so they cannot disagree.
public class LeaverRuleUtilityTests
{
    private static MatchData Match(bool lan, bool tournament)
    {
        return new MatchData { is_lan = lan, is_tournament_match = tournament };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_Online_Leaver_Is_An_Abandon_Tournament_Or_Not(bool tournament)
    {
        Assert.Equal(
            eLeaverRule.Abandon,
            LeaverRuleUtility.For(Match(lan: false, tournament: tournament), offline: false)
        );
    }

    [Fact]
    public void A_Lan_Leaver_Is_Only_An_Abandon_Once_Their_Team_Surrenders()
    {
        Assert.Equal(
            eLeaverRule.AbandonOnSurrender,
            LeaverRuleUtility.For(Match(lan: true, tournament: false), offline: false)
        );
    }

    [Fact]
    public void A_Lan_Tournament_Match_Has_No_Surrender_And_No_Abandon()
    {
        Assert.Equal(
            eLeaverRule.NoSurrender,
            LeaverRuleUtility.For(Match(lan: true, tournament: true), offline: false)
        );
    }

    // Offline nothing reaches an organizer, so the tournament flag changes nothing.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void An_Offline_Match_Is_Played_By_The_Lan_Rule(bool lan, bool tournament)
    {
        Assert.Equal(
            eLeaverRule.AbandonOnSurrender,
            LeaverRuleUtility.For(Match(lan, tournament), offline: true)
        );
    }

    [Fact]
    public void A_Server_With_No_Match_Is_Not_On_A_Lan()
    {
        Assert.Equal(eLeaverRule.Abandon, LeaverRuleUtility.For(null, offline: false));
        Assert.Equal(eLeaverRule.AbandonOnSurrender, LeaverRuleUtility.For(null, offline: true));
    }
}
