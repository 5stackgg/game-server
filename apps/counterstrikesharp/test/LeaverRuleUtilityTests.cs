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

    [Theory]
    [InlineData(eLeaverRule.Abandon)]
    [InlineData(eLeaverRule.AbandonOnSurrender)]
    [InlineData(eLeaverRule.NoSurrender)]
    public void Every_Match_Times_A_Player_Who_Drops_During_Play(eLeaverRule rule)
    {
        Assert.True(LeaverRuleUtility.TimesLeaver(rule, inPlay: true, mapOver: false));
    }

    // Warmup, knife and the wait for the next map: no vote and no abandon can
    // come of a drop there, but a LAN tournament's organizers are still told.
    [Theory]
    [InlineData(eLeaverRule.Abandon, false)]
    [InlineData(eLeaverRule.AbandonOnSurrender, false)]
    [InlineData(eLeaverRule.NoSurrender, true)]
    public void Only_A_Lan_Tournament_Match_Times_A_Player_Who_Drops_Outside_Play(
        eLeaverRule rule,
        bool timed
    )
    {
        Assert.Equal(timed, LeaverRuleUtility.TimesLeaver(rule, inPlay: false, mapOver: false));
    }

    // Everyone leaves a map that is over.
    [Theory]
    [InlineData(eLeaverRule.Abandon)]
    [InlineData(eLeaverRule.AbandonOnSurrender)]
    [InlineData(eLeaverRule.NoSurrender)]
    public void No_Match_Times_A_Player_Who_Leaves_A_Map_That_Is_Over(eLeaverRule rule)
    {
        Assert.False(LeaverRuleUtility.TimesLeaver(rule, inPlay: false, mapOver: true));
    }

    [Theory]
    [InlineData(eMapStatus.Warmup, eMapStatus.Knife)]
    [InlineData(eMapStatus.Warmup, eMapStatus.Live)]
    [InlineData(eMapStatus.Warmup, eMapStatus.Paused)]
    [InlineData(eMapStatus.Knife, eMapStatus.Live)]
    public void Every_Match_Times_The_Players_Missing_As_A_Map_Starts_Play(
        eMapStatus from,
        eMapStatus to
    )
    {
        foreach (eLeaverRule rule in Enum.GetValues<eLeaverRule>())
        {
            Assert.True(LeaverRuleUtility.TimesAbsentPlayers(rule, from, to, mapOrder: 1));
        }
    }

    // A resume, overtime, a map that ends and a server that starts into play
    // start nothing: whoever is missing then was timed, or left where they are.
    [Theory]
    [InlineData(eMapStatus.Paused, eMapStatus.Live)]
    [InlineData(eMapStatus.Live, eMapStatus.Paused)]
    [InlineData(eMapStatus.Live, eMapStatus.Overtime)]
    [InlineData(eMapStatus.Live, eMapStatus.WaitingForTV)]
    [InlineData(eMapStatus.Unknown, eMapStatus.Live)]
    [InlineData(eMapStatus.Unknown, eMapStatus.Knife)]
    [InlineData(eMapStatus.Knife, eMapStatus.Warmup)]
    public void Nothing_But_A_Map_Starting_Play_Times_The_Missing_Players(
        eMapStatus from,
        eMapStatus to
    )
    {
        foreach (eLeaverRule rule in Enum.GetValues<eLeaverRule>())
        {
            Assert.False(LeaverRuleUtility.TimesAbsentPlayers(rule, from, to, mapOrder: 1));
        }
    }

    // A player who left while the last map was over is first missed as the next
    // map loads into warmup, where only a LAN tournament times anyone.
    [Theory]
    [InlineData(eLeaverRule.Abandon, false)]
    [InlineData(eLeaverRule.AbandonOnSurrender, false)]
    [InlineData(eLeaverRule.NoSurrender, true)]
    public void Only_A_Lan_Tournament_Times_The_Players_Missing_From_A_Later_Maps_Warmup(
        eLeaverRule rule,
        bool timed
    )
    {
        Assert.Equal(
            timed,
            LeaverRuleUtility.TimesAbsentPlayers(
                rule,
                eMapStatus.Unknown,
                eMapStatus.Warmup,
                mapOrder: 2
            )
        );
        Assert.Equal(
            timed,
            LeaverRuleUtility.TimesAbsentPlayers(
                rule,
                eMapStatus.UploadingDemo,
                eMapStatus.Warmup,
                mapOrder: 2
            )
        );
    }

    // Nobody has missed anything by the first map's warmup.
    [Theory]
    [InlineData(eMapStatus.Unknown)]
    [InlineData(eMapStatus.Live)]
    public void A_Lan_Tournament_Times_Nobody_From_The_First_Maps_Warmup(eMapStatus from)
    {
        Assert.False(
            LeaverRuleUtility.TimesAbsentPlayers(
                eLeaverRule.NoSurrender,
                from,
                eMapStatus.Warmup,
                mapOrder: 1
            )
        );
    }
}
