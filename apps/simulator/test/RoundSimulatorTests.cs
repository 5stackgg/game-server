using FiveStack.Enums;
using FiveStack.Simulator;
using Xunit;

public class RoundSimulatorTests
{
    private static readonly Guid MapId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static List<SimPlayer> Team(
        string prefix,
        Side side,
        int size,
        BuyTier tier,
        Random random
    )
    {
        var players = Enumerable
            .Range(0, size)
            .Select(index => new SimPlayer
            {
                SteamId = $"{prefix}{index}",
                Name = $"{prefix}{index}",
                LineupId = Guid.Empty,
                Money = tier == BuyTier.Full ? 6000 : 800,
            })
            .ToList();

        Economy.Buy(players, side, tier, random);

        return players;
    }

    private static (RoundOutcome Outcome, RoundSetup Setup) Play(
        int seed,
        int size = 5,
        BuyTier tier = BuyTier.Full
    )
    {
        var random = new Random(seed);
        var setup = new RoundSetup
        {
            Round = 3,
            MatchMapId = MapId,
            MapName = "de_inferno",
            Terrorists = Team("t", Side.T, size, tier, random),
            CounterTerrorists = Team("ct", Side.CT, size, tier, random),
        };

        return (RoundSimulator.Play(setup, random), setup);
    }

    private static string Steam(PlannedEvent planned, string key)
    {
        return (string)planned.Data[key];
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void EveryRoundIsConsistentWithItsResult(int size)
    {
        for (int seed = 0; seed < 400; seed++)
        {
            (RoundOutcome outcome, RoundSetup setup) = Play(
                seed,
                size,
                seed % 3 == 0 ? BuyTier.Pistol : BuyTier.Full
            );

            int terroristsAlive = setup.Terrorists.Count(player => player.Alive);
            int counterTerroristsAlive = setup.CounterTerrorists.Count(player => player.Alive);
            List<string> objectives = outcome
                .Events.Where(planned => planned.Name == "objective")
                .Select(planned => (string)planned.Data["type"])
                .ToList();

            switch (outcome.Reason)
            {
                case eWinReason.TerroristsWin:
                    Assert.Equal(Side.T, outcome.Winner);
                    Assert.Equal(0, counterTerroristsAlive);
                    break;
                case eWinReason.CTsWin:
                    Assert.Equal(Side.CT, outcome.Winner);
                    Assert.Equal(0, terroristsAlive);
                    Assert.Empty(objectives);
                    break;
                case eWinReason.BombExploded:
                    Assert.Equal(Side.T, outcome.Winner);
                    Assert.Equal(["Planted", "Exploded"], objectives);
                    Assert.True(counterTerroristsAlive > 0);
                    break;
                case eWinReason.BombDefused:
                    Assert.Equal(Side.CT, outcome.Winner);
                    Assert.Equal(["Planted", "Defused"], objectives);
                    break;
                case eWinReason.TimeRanOut:
                    Assert.Equal(Side.CT, outcome.Winner);
                    Assert.Empty(objectives);
                    Assert.True(terroristsAlive > 0 && counterTerroristsAlive > 0);
                    break;
                default:
                    Assert.Fail($"unexpected reason {outcome.Reason}");
                    break;
            }
        }
    }

    [Fact]
    public void NobodyActsAfterTheyDie()
    {
        for (int seed = 0; seed < 400; seed++)
        {
            (RoundOutcome outcome, _) = Play(seed);

            var diedAt = new Dictionary<string, double>();

            foreach (PlannedEvent planned in outcome.Events)
            {
                // a grenade's damage outlives whoever threw it
                bool grenade =
                    planned.Data.TryGetValue("weapon", out object? weapon)
                    && weapon is "inferno" or "hegrenade";

                string? actor = planned.Name switch
                {
                    "damage" or "kill" when grenade => null,
                    "damage" or "kill" or "utility" or "flash" => Steam(
                        planned,
                        "attacker_steam_id"
                    ),
                    "objective" when (string)planned.Data["type"] != "Exploded" => Steam(
                        planned,
                        "player_steam_id"
                    ),
                    _ => null,
                };

                if (actor != null)
                {
                    Assert.False(
                        diedAt.ContainsKey(actor),
                        $"seed {seed}: {actor} acted in {planned.Name} after dying"
                    );
                }

                if (planned.Name == "kill")
                {
                    string victim = Steam(planned, "attacked_steam_id");
                    Assert.False(diedAt.ContainsKey(victim), $"seed {seed}: {victim} died twice");
                    diedAt[victim] = planned.At;
                }
            }
        }
    }

    [Fact]
    public void DamageAddsUpToTheHealthEachPlayerLost()
    {
        for (int seed = 0; seed < 400; seed++)
        {
            (RoundOutcome outcome, RoundSetup setup) = Play(seed);

            foreach (SimPlayer player in setup.Terrorists.Concat(setup.CounterTerrorists))
            {
                int dealt = outcome
                    .Events.Where(planned =>
                        planned.Name == "damage"
                        && Steam(planned, "attacked_steam_id") == player.SteamId
                    )
                    .Sum(planned => (int)planned.Data["damage"]);

                Assert.Equal(100 - Math.Max(0, player.Health), dealt);
            }
        }
    }

    [Fact]
    public void KillsAndAssistsAreAgainstTheOtherSide()
    {
        for (int seed = 0; seed < 400; seed++)
        {
            (RoundOutcome outcome, _) = Play(seed);

            foreach (
                PlannedEvent planned in outcome.Events.Where(planned =>
                    planned.Name is "kill" or "assist"
                )
            )
            {
                Assert.NotEqual(planned.Data["attacker_team"], planned.Data["attacked_team"]);
                Assert.NotEqual(
                    Steam(planned, "attacker_steam_id"),
                    Steam(planned, "attacked_steam_id")
                );
            }

            foreach (
                PlannedEvent assist in outcome.Events.Where(planned => planned.Name == "assist")
            )
            {
                PlannedEvent kill = outcome.Events.Last(planned =>
                    planned.Name == "kill"
                    && planned.At <= assist.At
                    && Steam(planned, "attacked_steam_id") == Steam(assist, "attacked_steam_id")
                );

                Assert.NotEqual(
                    Steam(kill, "attacker_steam_id"),
                    Steam(assist, "attacker_steam_id")
                );
                Assert.Equal(kill.Data["attacker_team"], assist.Data["attacker_team"]);
            }
        }
    }

    [Fact]
    public void EventsCarryTheRoundAndMapAndComeInOrder()
    {
        (RoundOutcome outcome, _) = Play(7);

        Assert.NotEmpty(outcome.Events);
        Assert.All(outcome.Events, planned => Assert.Equal(3, planned.Data["round"]));
        Assert.All(outcome.Events, planned => Assert.Equal(MapId, planned.Data["match_map_id"]));
        Assert.Equal(outcome.Events.OrderBy(planned => planned.At).ToList(), outcome.Events);
        Assert.All(outcome.Events, planned => Assert.True(planned.At <= outcome.Duration + 1));
    }

    [Fact]
    public void BothSidesWinAndEveryEndingHappens()
    {
        var outcomes = Enumerable.Range(0, 600).Select(seed => Play(seed).Outcome).ToList();

        Assert.Equivalent(
            new[]
            {
                eWinReason.TerroristsWin,
                eWinReason.CTsWin,
                eWinReason.BombExploded,
                eWinReason.BombDefused,
                eWinReason.TimeRanOut,
            },
            outcomes.Select(outcome => outcome.Reason).Distinct()
        );
        Assert.InRange(outcomes.Count(outcome => outcome.Winner == Side.T), 200, 400);
    }
}
