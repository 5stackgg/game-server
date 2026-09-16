using System.Text.Json;
using FiveStack.Entities;
using Xunit;

// The panel sizes a match from its custom game mode by sending
// options.min_players_per_lineup. If that field does not survive
// deserialization the server silently falls back to the type's 10/4/2 and sits
// in warmup waiting for players who were never rostered -- which looks exactly
// like a panel bug from the outside. Pinned here.
public class FlexibleLineupSizeTests
{
    private static MatchData Match(string payload) =>
        JsonSerializer.Deserialize<MatchData>(payload)!;

    // A 1v1 custom mode on a Wingman-typed match: the mode says one a side, so
    // the server must expect 2, not the 4 the type would imply.
    private const string OneVOneCustomMode =
        """
        {
          "options": {
            "type": "Wingman",
            "min_players_per_lineup": 1,
            "number_of_substitutes": 0
          },
          "lineup_1": { "lineup_players": [ { "steam_id": "1" } ] },
          "lineup_2": { "lineup_players": [ { "steam_id": "2" } ] }
        }
        """;

    private const string PlainWingman =
        """
        {
          "options": { "type": "Wingman", "number_of_substitutes": 0 },
          "lineup_1": { "lineup_players": [ { "steam_id": "1" }, { "steam_id": "2" } ] },
          "lineup_2": { "lineup_players": [ { "steam_id": "3" }, { "steam_id": "4" } ] }
        }
        """;

    [Fact]
    public void CustomModeSizeIsDeserialized()
    {
        Assert.Equal(1, Match(OneVOneCustomMode).options.min_players_per_lineup);
    }

    // Absent on every match the panel has ever sent before this feature, and on
    // every match without a sized mode. Null is what makes the fallback fire.
    [Fact]
    public void AbsentFieldIsNull()
    {
        Assert.Null(Match(PlainWingman).options.min_players_per_lineup);
    }

    // An uneven short-handed start: the panel records the SMALLER side in
    // min_players_per_lineup (its gates apply that to both lineups, so a 1v2 has
    // to record 1 or the short side never clears) and the real total separately.
    private const string UnevenOneVTwo =
        """
        {
          "options": {
            "type": "Competitive",
            "min_players_per_lineup": 1,
            "expected_players": 3,
            "number_of_substitutes": 0
          },
          "lineup_1": { "lineup_players": [ { "steam_id": "1" } ] },
          "lineup_2": { "lineup_players": [ { "steam_id": "2" }, { "steam_id": "3" } ] }
        }
        """;

    // Mirrors MatchManager.GetExpectedPlayerCount(), in precedence order: the
    // explicit total, then the per-lineup snapshot doubled, then the type.
    private static int Expected(MatchData m) =>
        m.options.expected_players
            ?? (m.options.min_players_per_lineup != null
                ? m.options.min_players_per_lineup.Value * 2
                : m.options.type switch { "Wingman" => 4, "Duel" => 2, _ => 10 });

    [Fact]
    public void UnevenStartWaitsForEveryone()
    {
        // The bug this pins: min x 2 gives 2 here, so the match would go live
        // with the third player still connecting.
        Assert.Equal(3, Expected(Match(UnevenOneVTwo)));
    }

    [Fact]
    public void UnevenStartStillRecordsTheSmallerSideForTheGates()
    {
        Assert.Equal(1, Match(UnevenOneVTwo).options.min_players_per_lineup);
    }

    [Fact]
    public void EvenStartNeedsNoTotal()
    {
        Assert.Null(Match(OneVOneCustomMode).options.expected_players);
    }

    [Fact]
    public void SizedModeExpectsTwoPlayers()
    {
        Assert.Equal(2, Expected(Match(OneVOneCustomMode)));
    }

    [Fact]
    public void PlainWingmanStillExpectsFour()
    {
        Assert.Equal(4, Expected(Match(PlainWingman)));
    }
}
