using FiveStack.Entities;
using FiveStack.Utilities;
using Xunit;

public class MatchUtilityTests
{
    private static MatchData BuildMatch()
    {
        return new MatchData
        {
            id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            current_match_map_id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            lineup_1 = new MatchLineUp
            {
                lineup_players = new List<MatchMember>
                {
                    new MatchMember { steam_id = "76561198000000001", name = "Real" },
                },
            },
            lineup_2 = new MatchLineUp
            {
                lineup_players = new List<MatchMember>
                {
                    new MatchMember { steam_id = null, placeholder_name = "AceBot" },
                },
            },
        };
    }

    [Fact]
    public void GetMemberFromLineup_MatchesBySteamId()
    {
        MatchMember? member = MatchUtility.GetMemberFromLineup(
            BuildMatch(),
            "76561198000000001",
            "ignored"
        );
        Assert.NotNull(member);
        Assert.Equal("Real", member!.name);
    }

    [Fact]
    public void GetMemberFromLineup_MatchesPlaceholderByNamePrefix()
    {
        MatchMember? member = MatchUtility.GetMemberFromLineup(BuildMatch(), "9999", "Ace");
        Assert.NotNull(member);
        Assert.Equal("AceBot", member!.placeholder_name);
    }

    [Fact]
    public void GetMemberFromLineup_ReturnsNullWhenNoMatch()
    {
        Assert.Null(MatchUtility.GetMemberFromLineup(BuildMatch(), "9999", "Nobody"));
    }

    [Fact]
    public void HasPlaceholderMembers_TrueWhenAnyNullSteamId()
    {
        Assert.True(MatchUtility.HasPlaceholderMembers(BuildMatch()));
    }

    [Fact]
    public void HasPlaceholderMembers_FalseWhenAllHaveSteamId()
    {
        MatchData match = BuildMatch();
        match.lineup_2.lineup_players[0].steam_id = "76561198000000002";
        Assert.False(MatchUtility.HasPlaceholderMembers(match));
    }

    [Fact]
    public void GetSafeMatchPrefix_StripsDashesAndJoinsIds()
    {
        string prefix = MatchUtility.GetSafeMatchPrefix(BuildMatch());
        Assert.DoesNotContain("-", prefix);
        Assert.Equal("11111111111111111111111111111111_22222222222222222222222222222222", prefix);
    }

    [Fact]
    public void RosterSteamIds_SpansBothLineups()
    {
        MatchData match = BuildMatch();
        match.lineup_2.lineup_players[0].steam_id = "76561198000000002";
        match.lineup_2.lineup_players[0].placeholder_name = "";

        HashSet<string> roster = MatchUtility.RosterSteamIds(match);

        Assert.Equal(new HashSet<string> { "76561198000000001", "76561198000000002" }, roster);
    }

    [Fact]
    public void RosterSteamIds_SkipsPlaceholderSeats()
    {
        HashSet<string> roster = MatchUtility.RosterSteamIds(BuildMatch());

        Assert.Equal(new HashSet<string> { "76561198000000001" }, roster);
    }

    [Fact]
    public void RosterSteamIds_ExcludesNonRosterSteamIds()
    {
        HashSet<string> roster = MatchUtility.RosterSteamIds(BuildMatch());

        Assert.DoesNotContain("76561198000009999", roster);
    }

    private static readonly Guid Lineup1Id = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Lineup2Id = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static MatchData BuildTeamChatMatch()
    {
        return new MatchData
        {
            lineup_1 = new MatchLineUp
            {
                id = Lineup1Id,
                coach_steam_id = "76561198000000011",
                lineup_players = new List<MatchMember>
                {
                    new MatchMember
                    {
                        steam_id = "76561198000000001",
                        name = "Real",
                        match_lineup_id = Lineup1Id,
                    },
                },
            },
            lineup_2 = new MatchLineUp
            {
                id = Lineup2Id,
                coach_steam_id = "76561198000000022",
                lineup_players = new List<MatchMember>
                {
                    new MatchMember
                    {
                        steam_id = null,
                        placeholder_name = "AceBot",
                        match_lineup_id = Lineup2Id,
                    },
                },
            },
        };
    }

    private static MatchData BuildShadowedMemberMatch()
    {
        MatchData match = BuildTeamChatMatch();
        match.lineup_1.lineup_players.Add(
            new MatchMember
            {
                steam_id = null,
                placeholder_name = "Steve",
                match_lineup_id = Lineup1Id,
            }
        );
        match.lineup_2.lineup_players.Add(
            new MatchMember
            {
                steam_id = "76561198000000002",
                name = "St",
                match_lineup_id = Lineup2Id,
            }
        );
        return match;
    }

    [Fact]
    public void GetTeamChatLineupId_ResolvesMemberLineup()
    {
        Assert.Equal(
            Lineup1Id.ToString(),
            MatchUtility.GetTeamChatLineupId(BuildTeamChatMatch(), "76561198000000001", "ignored")
        );
    }

    [Fact]
    public void GetTeamChatLineupId_ResolvesPlaceholderByNamePrefix()
    {
        Assert.Equal(
            Lineup2Id.ToString(),
            MatchUtility.GetTeamChatLineupId(BuildTeamChatMatch(), "9999", "Ace")
        );
    }

    [Fact]
    public void GetTeamChatLineupId_PrefersExactSteamIdOverPlaceholderPrefix()
    {
        Assert.Equal(
            Lineup2Id.ToString(),
            MatchUtility.GetTeamChatLineupId(BuildShadowedMemberMatch(), "76561198000000002", "St")
        );
    }

    [Theory]
    [InlineData("76561198000000011", "33333333-3333-3333-3333-333333333333")]
    [InlineData("76561198000000022", "44444444-4444-4444-4444-444444444444")]
    public void GetTeamChatLineupId_ResolvesCoachLineup(string steamId, string expected)
    {
        Assert.Equal(
            expected,
            MatchUtility.GetTeamChatLineupId(BuildTeamChatMatch(), steamId, "Coach")
        );
    }

    [Fact]
    public void GetTeamChatLineupId_PrefersMemberLineupOverCoachSeat()
    {
        MatchData match = BuildTeamChatMatch();
        match.lineup_2.coach_steam_id = "76561198000000001";

        Assert.Equal(
            Lineup1Id.ToString(),
            MatchUtility.GetTeamChatLineupId(match, "76561198000000001", "Real")
        );
    }

    [Fact]
    public void GetTeamChatLineupId_IgnoresCoachWhenCoachesAreDisabled()
    {
        MatchData match = BuildTeamChatMatch();
        match.options.coaches = false;

        Assert.Null(MatchUtility.GetTeamChatLineupId(match, "76561198000000011", "Coach"));
    }

    [Fact]
    public void GetTeamChatLineupId_ReturnsNullForUnknownPlayer()
    {
        Assert.Null(
            MatchUtility.GetTeamChatLineupId(BuildTeamChatMatch(), "76561198000009999", "Nobody")
        );
    }

    [Fact]
    public void GetTeamChatLineupId_ReturnsNullWhenMemberLineupIsEmpty()
    {
        MatchData match = BuildTeamChatMatch();
        match.lineup_1.lineup_players[0].match_lineup_id = Guid.Empty;

        Assert.Null(MatchUtility.GetTeamChatLineupId(match, "76561198000000001", "Real"));
    }

    [Fact]
    public void GetTeamChatLineupId_ReturnsNullWhenCoachLineupIsEmpty()
    {
        MatchData match = BuildTeamChatMatch();
        match.lineup_2.id = Guid.Empty;

        Assert.Null(MatchUtility.GetTeamChatLineupId(match, "76561198000000022", "Coach"));
    }

    [Fact]
    public void GetTeamChatLineupId_DoesNotMatchAnEmptySteamIdToAnEmptyCoachSeat()
    {
        MatchData match = BuildTeamChatMatch();
        match.lineup_1.coach_steam_id = null!;
        match.lineup_2.coach_steam_id = "";

        Assert.Null(MatchUtility.GetTeamChatLineupId(match, "", "Nobody"));
    }

    [Fact]
    public void GetTeamChatRelayLineupId_ReturnsNullWhenApiDoesNotAdvertiseIt()
    {
        Assert.Null(
            MatchUtility.GetTeamChatRelayLineupId(BuildTeamChatMatch(), "76561198000000001", "Real")
        );
    }

    [Fact]
    public void GetTeamChatRelayLineupId_ResolvesWhenApiAdvertisesIt()
    {
        MatchData match = BuildTeamChatMatch();
        match.relay_team_chat = true;

        Assert.Equal(
            Lineup1Id.ToString(),
            MatchUtility.GetTeamChatRelayLineupId(match, "76561198000000001", "Real")
        );
    }

    [Fact]
    public void GetTeamChatRelayLineupId_ReturnsNullForGaggedMember()
    {
        MatchData match = BuildTeamChatMatch();
        match.relay_team_chat = true;
        match.lineup_1.lineup_players[0].is_gagged = true;

        Assert.Null(MatchUtility.GetTeamChatRelayLineupId(match, "76561198000000001", "Real"));
    }

    [Fact]
    public void GetTeamChatRelayLineupId_ReturnsNullForGaggedMemberShadowedByPlaceholder()
    {
        MatchData match = BuildShadowedMemberMatch();
        match.relay_team_chat = true;
        match.lineup_2.lineup_players[1].is_gagged = true;

        Assert.Null(MatchUtility.GetTeamChatRelayLineupId(match, "76561198000000002", "St"));
    }

    [Fact]
    public void GetTeamChatRelayLineupId_ReturnsNullWhenLineupDoesNotResolve()
    {
        MatchData match = BuildTeamChatMatch();
        match.relay_team_chat = true;

        Assert.Null(MatchUtility.GetTeamChatRelayLineupId(match, "76561198000009999", "Nobody"));
    }

    [Fact]
    public void ChatEvent_KeepsTheAllChatShape()
    {
        (string eventName, Dictionary<string, object> data) = MatchUtility.ChatEvent(
            "76561198000000001",
            "gl hf"
        );

        Assert.Equal("chat", eventName);
        Assert.Equal(
            new[] { "message", "player" },
            data.Keys.OrderBy(key => key, StringComparer.Ordinal)
        );
        Assert.Equal("76561198000000001", data["player"]);
        Assert.Equal("gl hf", data["message"]);
    }

    // an api that predates team chat drops an event it has no handler for,
    // but would post anything sent as chat to the room both teams read
    [Theory]
    [InlineData("76561198000000001", "Real", "33333333-3333-3333-3333-333333333333")]
    [InlineData("76561198000000011", "Coach", "33333333-3333-3333-3333-333333333333")]
    [InlineData("76561198000000022", "Coach", "44444444-4444-4444-4444-444444444444")]
    public void TeamChatEvent_SendsTeamChatUnderItsOwnEvent(
        string steamId,
        string playerName,
        string lineupId
    )
    {
        MatchData match = BuildTeamChatMatch();
        match.relay_team_chat = true;

        (string Event, Dictionary<string, object> Data)? teamChat = MatchUtility.TeamChatEvent(
            match,
            steamId,
            playerName,
            "stack b"
        );

        Assert.NotNull(teamChat);
        Assert.Equal("teamChat", teamChat.Value.Event);
        Assert.Equal(
            new[] { "lineupId", "message", "player" },
            teamChat.Value.Data.Keys.OrderBy(key => key, StringComparer.Ordinal)
        );
        Assert.Equal(steamId, teamChat.Value.Data["player"]);
        Assert.Equal("stack b", teamChat.Value.Data["message"]);
        Assert.Equal(lineupId, teamChat.Value.Data["lineupId"]);
    }

    [Fact]
    public void TeamChatEvent_SendsNothingWhenApiDoesNotAdvertiseIt()
    {
        Assert.Null(
            MatchUtility.TeamChatEvent(BuildTeamChatMatch(), "76561198000000001", "Real", "stack b")
        );
    }

    [Fact]
    public void TeamChatEvent_SendsNothingForGaggedMember()
    {
        MatchData match = BuildTeamChatMatch();
        match.relay_team_chat = true;
        match.lineup_1.lineup_players[0].is_gagged = true;

        Assert.Null(MatchUtility.TeamChatEvent(match, "76561198000000001", "Real", "stack b"));
    }

    [Fact]
    public void TeamChatEvent_SendsNothingForPlayerOffTheRoster()
    {
        MatchData match = BuildTeamChatMatch();
        match.relay_team_chat = true;

        Assert.Null(MatchUtility.TeamChatEvent(match, "76561198000009999", "Caster", "stack b"));
    }
}
