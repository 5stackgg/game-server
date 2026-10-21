using FiveStack;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// A captain who drops in warmup keeps the flag, and the flag is what hands
// them the spot back on reconnect. Once a teammate claims it, the match data
// on the server has to stop naming the one who left before the api's copy
// comes back, or reconnecting in that window takes the spot back.
public class CaptainSystemTests
{
    private const string Left = "76561198000000001";
    private const string Teammate = "76561198000000002";
    private const string OtherCaptain = "76561198000000003";

    private static MatchData BuildMatch()
    {
        return new MatchData
        {
            lineup_1 = new MatchLineUp
            {
                lineup_players = new List<MatchMember>
                {
                    new MatchMember { steam_id = Left, name = "Left", captain = true },
                    new MatchMember { steam_id = Teammate, name = "Teammate" },
                },
            },
            lineup_2 = new MatchLineUp
            {
                lineup_players = new List<MatchMember>
                {
                    new MatchMember
                    {
                        steam_id = OtherCaptain,
                        name = "OtherCaptain",
                        captain = true,
                    },
                },
            },
        };
    }

    private static bool IsFlagged(MatchData match, string steamId)
    {
        return MatchUtility.GetMemberFromLineup(match, steamId, "")!.captain;
    }

    [Fact]
    public void A_New_Captain_Takes_The_Flag_From_The_One_Who_Left()
    {
        MatchData match = BuildMatch();

        CaptainSystem.HandCaptainFlag(match, Teammate, "Teammate");

        Assert.True(IsFlagged(match, Teammate));
        Assert.False(IsFlagged(match, Left));
    }

    [Fact]
    public void Leaves_The_Other_Lineups_Captain_Alone()
    {
        MatchData match = BuildMatch();

        CaptainSystem.HandCaptainFlag(match, Teammate, "Teammate");

        Assert.True(IsFlagged(match, OtherCaptain));
    }

    [Fact]
    public void A_Player_Outside_The_Lineups_Changes_Nothing()
    {
        MatchData match = BuildMatch();

        CaptainSystem.HandCaptainFlag(match, "76561198000000009", "Stranger");

        Assert.True(IsFlagged(match, Left));
        Assert.False(IsFlagged(match, Teammate));
        Assert.True(IsFlagged(match, OtherCaptain));
    }

    [Fact]
    public void A_Warmup_Disconnect_Holds_The_Captaincy()
    {
        Assert.True(CaptainSystem.HoldsCaptainThroughDisconnect(isKnife: false));
    }

    [Fact]
    public void A_Knife_Round_Disconnect_Releases_It()
    {
        Assert.False(CaptainSystem.HoldsCaptainThroughDisconnect(isKnife: true));
    }

    [Fact]
    public void Releasing_The_Captaincy_Clears_The_Flag()
    {
        MatchData match = BuildMatch();

        CaptainSystem.ReleaseCaptainFlag(match, Left, "Left");

        Assert.False(IsFlagged(match, Left));
        Assert.True(IsFlagged(match, OtherCaptain));
    }

    [Theory]
    [InlineData(eMapStatus.Warmup)]
    [InlineData(eMapStatus.Paused)]
    public void Going_Live_Before_A_Round_Is_Played_Picks_Captains(eMapStatus from)
    {
        Assert.True(CaptainSystem.PicksCaptainsGoingLive(from, roundsPlayed: 0));
    }

    [Theory]
    [InlineData(eMapStatus.Paused, 5)]
    [InlineData(eMapStatus.Knife, 0)]
    [InlineData(eMapStatus.Unknown, 0)]
    [InlineData(eMapStatus.Unknown, 12)]
    public void Going_Live_Mid_Match_Or_From_The_Knife_Round_Keeps_The_Captains(
        eMapStatus from,
        int roundsPlayed
    )
    {
        Assert.False(CaptainSystem.PicksCaptainsGoingLive(from, roundsPlayed));
    }
}
