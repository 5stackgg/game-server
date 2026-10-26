using System.Text.RegularExpressions;
using FiveStack.Entities;

namespace FiveStack.Simulator;

// What a match asks the simulator to play, from a `sim:` word in either
// lineup's name or tag: `sim:3-1` (that lineup takes the series 3-1),
// `sim:tie`, `sim:surrender`, `sim:noshow`, `sim:disconnect`, `sim:abandon`.
// The lineup carrying the word is the one the scenario happens to.
public sealed partial record Scenario
{
    public Guid Subject { get; init; }
    public int? Wins { get; init; }
    public int? Losses { get; init; }
    public bool Tie { get; init; }
    public bool Surrender { get; init; }
    public bool NoShow { get; init; }
    public bool Disconnect { get; init; }
    public bool Abandon { get; init; }

    public bool IsEmpty => Wins == null && !Tie && !Surrender && !NoShow && !Disconnect && !Abandon;

    [GeneratedRegex(@"\bsim:([a-z0-9-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Word();

    [GeneratedRegex(@"^(\d)-(\d)$")]
    private static partial Regex Result();

    public static Scenario From(MatchData match)
    {
        var scenario = new Scenario();

        foreach (
            (MatchLineUp lineup, Guid id) in new[]
            {
                (match.lineup_1, match.lineup_1_id),
                (match.lineup_2, match.lineup_2_id),
            }
        )
        {
            foreach (Match word in Word().Matches($"{lineup.name} {lineup.tag}"))
            {
                string value = word.Groups[1].Value.ToLowerInvariant();
                Match result = Result().Match(value);

                scenario = scenario with
                {
                    Subject = id,
                    Wins = result.Success ? int.Parse(result.Groups[1].Value) : scenario.Wins,
                    Losses = result.Success ? int.Parse(result.Groups[2].Value) : scenario.Losses,
                    Tie = scenario.Tie || value == "tie",
                    Surrender = scenario.Surrender || value == "surrender",
                    NoShow = scenario.NoShow || value == "noshow",
                    Disconnect = scenario.Disconnect || value == "disconnect",
                    Abandon = scenario.Abandon || value == "abandon",
                };
            }
        }

        return scenario;
    }

    // Whether the subject wins each map, in order. The losses are spread
    // between the wins so the series is only decided on its last map.
    public List<bool>? SeriesPlan(int bestOf)
    {
        int toWin = bestOf / 2 + 1;

        if (
            Wins is not int wins
            || Losses is not int losses
            || Math.Max(wins, losses) != toWin
            || Math.Min(wins, losses) >= toWin
        )
        {
            return null;
        }

        var maps = new List<bool>();
        bool subjectsTurn = true;

        while (wins > 0 || losses > 0)
        {
            bool subjectWins =
                wins == 0 ? false
                : losses == 0 ? true
                : subjectsTurn;
            maps.Add(subjectWins);

            if (subjectWins)
            {
                wins--;
            }
            else
            {
                losses--;
            }

            subjectsTurn = !subjectsTurn;
        }

        return maps;
    }
}

// The lineup that wins each round of a map the scenario decides.
public static class MapScript
{
    public static List<Guid> Won(MatchOptions options, Guid winner, Guid loser, Random random)
    {
        int toWin = options.MaxRounds() / 2 + 1;
        int lost = random.Next(2, toWin - 1);

        List<Guid> rounds = Enumerable
            .Repeat(winner, toWin - 1)
            .Concat(Enumerable.Repeat(loser, lost))
            .OrderBy(_ => random.Next())
            .ToList();
        rounds.Add(winner);

        return rounds;
    }

    // Level at the end of regulation, which only ends the map without overtime.
    public static List<Guid> Tied(MatchOptions options, Guid lineup1, Guid lineup2, Random random)
    {
        int half = options.MaxRounds() / 2;

        return Enumerable
            .Repeat(lineup1, half)
            .Concat(Enumerable.Repeat(lineup2, half))
            .OrderBy(_ => random.Next())
            .ToList();
    }
}
