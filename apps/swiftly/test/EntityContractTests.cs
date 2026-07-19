using System.Text.Json;
using FiveStack.Entities;
using Xunit;

public class EntityContractTests
{
    private const string MatchPayload =
        """
        {
          "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
          "is_lan": false,
          "password": "connectme",
          "current_match_map_id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
          "lineup_1_id": "cccccccc-cccc-cccc-cccc-cccccccccccc",
          "lineup_2_id": "dddddddd-dddd-dddd-dddd-dddddddddddd",
          "options": {
            "mr": 12,
            "type": "Competitive",
            "overtime": true,
            "best_of": 3,
            "tv_delay": 105,
            "round_restart_delay": 7,
            "halftime_pausematch": true,
            "coaches": true,
            "number_of_substitutes": 2,
            "knife_round": true,
            "default_models": false,
            "ready_setting": "Players",
            "timeout_setting": "CoachAndPlayers",
            "tech_timeout_setting": "CoachAndPlayers",
            "use_playcast": false,
            "show_elo_ranks": true,
            "cfg_overrides": { "sv_cheats": "0" }
          },
          "lineup_1": {
            "id": "cccccccc-cccc-cccc-cccc-cccccccccccc",
            "name": "Team A",
            "tag": "A",
            "coach_steam_id": "76561198000000009",
            "lineup_players": [
              { "name": "p1", "role": "verified_user", "steam_id": "76561198000000001", "captain": true, "elo": 1500, "is_gagged": true },
              { "name": "p2", "role": "", "placeholder_name": "Bot", "steam_id": null, "captain": false, "elo": null }
            ]
          },
          "lineup_2": {
            "id": "dddddddd-dddd-dddd-dddd-dddddddddddd",
            "name": "Team B",
            "tag": "B",
            "coach_steam_id": "",
            "lineup_players": []
          },
          "match_maps": [
            {
              "id": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
              "map": { "name": "de_dust2", "workshop_map_id": "3070428676" },
              "order": 0,
              "status": "Live",
              "lineup_1_side": "CT",
              "lineup_2_side": "TERRORIST",
              "rounds": []
            }
          ]
        }
        """;

    private static MatchData Deserialize()
    {
        MatchData? match = JsonSerializer.Deserialize<MatchData>(MatchPayload);
        Assert.NotNull(match);
        return match!;
    }

    [Fact]
    public void TopLevelFields_Map()
    {
        MatchData match = Deserialize();
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), match.id);
        Assert.False(match.is_lan);
        Assert.Equal("connectme", match.password);
        Assert.Equal(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            match.current_match_map_id
        );
        Assert.Equal(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), match.lineup_1_id);
    }

    [Fact]
    public void Options_Map()
    {
        MatchOptions options = Deserialize().options;
        Assert.Equal(12, options.mr);
        Assert.Equal("Competitive", options.type);
        Assert.Equal(3, options.best_of);
        Assert.True(options.knife_round);
        Assert.Equal("Players", options.ready_setting);
        Assert.Equal("CoachAndPlayers", options.tech_timeout_setting);
        Assert.True(options.show_elo_ranks);
        Assert.False(options.default_models);
        Assert.Equal(7, options.round_restart_delay);
        Assert.True(options.halftime_pausematch);
        Assert.Equal("0", options.cfg_overrides["sv_cheats"]);
    }

    [Fact]
    public void Lineup_And_Members_Map()
    {
        MatchLineUp lineup = Deserialize().lineup_1;
        Assert.Equal("Team A", lineup.name);
        Assert.Equal("A", lineup.tag);
        Assert.Equal("76561198000000009", lineup.coach_steam_id);
        Assert.Equal(2, lineup.lineup_players.Count);

        MatchMember captain = lineup.lineup_players[0];
        Assert.Equal("76561198000000001", captain.steam_id);
        Assert.True(captain.captain);
        Assert.Equal(1500, captain.elo);
        Assert.True(captain.is_gagged);

        MatchMember placeholder = lineup.lineup_players[1];
        Assert.Null(placeholder.steam_id);
        Assert.Null(placeholder.elo);
        Assert.Equal("Bot", placeholder.placeholder_name);
    }

    [Fact]
    public void MatchMaps_Map()
    {
        MatchMap map = Assert.Single(Deserialize().match_maps);
        Assert.Equal("de_dust2", map.map.name);
        Assert.Equal("3070428676", map.map.workshop_map_id);
        Assert.Equal("Live", map.status);
        Assert.Equal("CT", map.lineup_1_side);
        Assert.Equal("TERRORIST", map.lineup_2_side);
    }

    [Fact]
    public void EmptyBody_MustBeLengthGuarded()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MatchData>(""));
    }
}
