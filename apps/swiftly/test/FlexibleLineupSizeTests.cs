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

    // Mirrors MatchManager.GetExpectedPlayerCount(): the snapshot wins, and it
    // counts starters a side x2 rather than the roster, so substitutes never
    // inflate what warmup waits for.
    private static int Expected(MatchData m) =>
        m.options.min_players_per_lineup != null
            ? m.options.min_players_per_lineup.Value * 2
            : m.options.type switch { "Wingman" => 4, "Duel" => 2, _ => 10 };

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
