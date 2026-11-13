using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// Only the lineups' own players ready up and are listed as not ready; a coach,
// caster or spectator readying would start the match a player short.
public class ReadyUtilityTests
{
    private const string LineupPlayer = "76561198000000001";
    private const string Outsider = "76561198000000009";

    private static bool CanReady(
        eReadySettings setting,
        string steamId,
        string name,
        bool isCoach = false,
        bool isCaptain = false
    )
    {
        var match = new MatchData
        {
            lineup_1 = new MatchLineUp
            {
                lineup_players = new List<MatchMember> { new MatchMember { steam_id = LineupPlayer } },
            },
            lineup_2 = new MatchLineUp
            {
                lineup_players = new List<MatchMember>
                {
                    new MatchMember { steam_id = null, placeholder_name = "Bob" },
                },
            },
        };

        return ReadyUtility.CanReady(setting, match, steamId, name, isCoach, isCaptain);
    }

    [Fact]
    public void A_Lineup_Player_Can_Ready()
    {
        Assert.True(CanReady(eReadySettings.Players, LineupPlayer, "Alice"));
    }

    [Fact]
    public void A_Player_Who_Joined_Under_A_Placeholder_Name_Can_Ready()
    {
        Assert.True(CanReady(eReadySettings.Players, Outsider, "Bob"));
    }

    [Fact]
    public void A_Player_Whose_Steam_Name_Fits_Their_Seat_Can_Ready()
    {
        Assert.True(CanReady(eReadySettings.Players, Outsider, "[TAG] bob"));
    }

    [Theory]
    [InlineData("coach", true)]
    [InlineData("caster", false)]
    [InlineData("spectator", false)]
    public void Someone_Off_The_Lineups_Cannot_Ready(string name, bool isCoach)
    {
        Assert.False(CanReady(eReadySettings.Players, Outsider, name, isCoach));
    }

    [Fact]
    public void A_Captain_Has_To_Be_On_A_Lineup()
    {
        Assert.True(CanReady(eReadySettings.Captains, LineupPlayer, "Alice", isCaptain: true));
        Assert.False(CanReady(eReadySettings.Captains, Outsider, "coach", true, true));
        Assert.False(CanReady(eReadySettings.Captains, LineupPlayer, "Alice"));
    }

    [Fact]
    public void Only_The_Coach_Readies_In_Coach_Mode()
    {
        Assert.True(CanReady(eReadySettings.Coach, Outsider, "coach", isCoach: true));
        Assert.False(CanReady(eReadySettings.Coach, LineupPlayer, "Alice", isCaptain: true));
    }

    [Fact]
    public void Nobody_Readies_In_Admin_Mode()
    {
        Assert.False(CanReady(eReadySettings.Admin, LineupPlayer, "Alice", true, true));
    }
}
