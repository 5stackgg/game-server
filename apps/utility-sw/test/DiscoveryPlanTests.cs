using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Xunit;

// The plan decides what a discovery server throws. The engine side only
// emits what it is told and reports where it landed, so a fake "engine" here
// is just a function from a seed to a landing.
public class DiscoveryPlanTests
{
    private static readonly Vec3 Target = new Vec3(1000f, 0f, 0f);

    private static DiscoveryJob Job(params DiscoveryCandidate[] candidates)
    {
        return new DiscoveryJob
        {
            id = "job",
            map = "de_test",
            target = Target,
            radius = 40f,
            candidates = candidates.ToList(),
            max_grenades = 400,
            max_seconds = 240,
        };
    }

    private static DiscoveryCandidate Candidate(string id, float pitch, float yaw)
    {
        return new DiscoveryCandidate
        {
            result_id = id,
            stand = new Vec3(0f, 0f, 0f),
            jump_throw = true,
            strength = "Full",
            pitch = pitch,
            yaw = yaw,
        };
    }

    // A pretend world where the landing is a smooth function of the aim with
    // its sweet spot at (pitch, yaw) = (best, 0): every degree off it is
    // `slope` units off the target, and pitch is twice as sensitive as yaw.
    private static DiscoveryLanding Fly(DiscoveryThrow thrown, float best, float slope)
    {
        float dx = (thrown.pitch - best) * slope * 2f;
        float dy = thrown.yaw * slope;

        return new DiscoveryLanding
        {
            landed = true,
            position = Target + new Vec3(dx, dy, 0f),
            bounces = 3,
            ticks = 320,
            path = new List<Vec3> { new Vec3(0f, 0f, 0f), Target },
        };
    }

    private static List<DiscoveryOutcome> Run(DiscoveryPlan plan, Func<DiscoveryThrow, DiscoveryLanding> world)
    {
        var outcomes = new List<DiscoveryOutcome>();

        for (int round = 0; round < 1000 && !plan.Done; round++)
        {
            List<DiscoveryThrow> batch = plan.Next(32);

            if (batch.Count == 0)
            {
                break;
            }

            foreach (DiscoveryThrow thrown in batch)
            {
                plan.Observe(thrown, world(thrown));
            }

            outcomes.AddRange(plan.TakeFinished());
        }

        return outcomes;
    }

    [Fact]
    public void AProposalThatLandsIsProbedAndKept()
    {
        var plan = new DiscoveryPlan(Job(Candidate("a", -40f, 0f)));

        List<DiscoveryOutcome> outcomes = Run(plan, thrown => Fly(thrown, -40f, 20f));

        DiscoveryOutcome outcome = Assert.Single(outcomes);
        Assert.Equal("verified", outcome.status);
        Assert.Equal(-40f, outcome.pitch);
        // Four probes a quarter degree out: pitch moves it 0.25 * 40 = 10.
        Assert.Equal(10f, outcome.forgiveness, 2);
        Assert.Equal(5, outcome.grenades);
        Assert.Equal(5000, outcome.flight_time_ms);
        Assert.NotEmpty(outcome.path);
    }

    // The flight model is out by a degree after a soft bounce; the plan finds
    // the real aim around it.
    [Fact]
    public void ANearMissIsSearchedUntilItLands()
    {
        var plan = new DiscoveryPlan(Job(Candidate("a", -41.2f, 0.6f)));

        List<DiscoveryOutcome> outcomes = Run(plan, thrown => Fly(thrown, -40f, 30f));

        DiscoveryOutcome outcome = Assert.Single(outcomes);
        Assert.Equal("verified", outcome.status);
        Assert.InRange(outcome.distance, 0f, 40f);
        Assert.InRange(outcome.grenades, 6, 5 + DiscoveryPlan.MaxRefineGrenades);
    }

    [Fact]
    public void AFarMissIsDroppedAtOnce()
    {
        var plan = new DiscoveryPlan(Job(Candidate("a", -60f, 0f)));

        List<DiscoveryOutcome> outcomes = Run(plan, thrown => Fly(thrown, -40f, 30f));

        DiscoveryOutcome outcome = Assert.Single(outcomes);
        Assert.Equal("missed", outcome.status);
        Assert.Equal(1, outcome.grenades);
        Assert.Empty(outcome.path);
    }

    [Fact]
    public void ASearchThatNeverLandsStopsAtItsGrenadeAllowance()
    {
        var plan = new DiscoveryPlan(Job(Candidate("a", -40f, 0f)));

        // Always 200 units out, whatever the aim.
        List<DiscoveryOutcome> outcomes = Run(
            plan,
            thrown => new DiscoveryLanding { landed = true, position = Target + new Vec3(200f, 0f, 0f) }
        );

        DiscoveryOutcome outcome = Assert.Single(outcomes);
        Assert.Equal("missed", outcome.status);
        Assert.InRange(outcome.grenades, 1, 1 + DiscoveryPlan.MaxRefineGrenades);
    }

    // A grenade the engine lost is not a hit, and a probe that is lost makes
    // the throw unforgiving rather than forgiving.
    [Fact]
    public void ALostGrenadeIsNeverAHit()
    {
        var plan = new DiscoveryPlan(Job(Candidate("a", -40f, 0f)));

        List<DiscoveryOutcome> outcomes = Run(
            plan,
            thrown =>
                thrown.kind == eDiscoveryThrow.Probe
                    ? new DiscoveryLanding { landed = false }
                    : Fly(thrown, -40f, 20f)
        );

        DiscoveryOutcome outcome = Assert.Single(outcomes);
        Assert.Equal("verified", outcome.status);
        Assert.Equal(-1f, outcome.forgiveness);
    }

    // The plan throws exactly the seed the plugin's own launch model gives the
    // candidate's technique, so what is verified is what a player reproduces.
    [Fact]
    public void ThrowsTheTechniquesOwnSeed()
    {
        DiscoveryCandidate candidate = Candidate("a", -55.324947f, -161.02596f);
        candidate.stand = new Vec3(908.68005f, -1036.0312f, -251.96875f);
        var plan = new DiscoveryPlan(Job(candidate));

        DiscoveryThrow thrown = Assert.Single(plan.Next(32));

        Assert.InRange(
            (thrown.seed.velocity - new Vec3(-327.06262f, -112.45098f, 853.15137f)).Length(),
            0f,
            0.5f
        );
    }

    [Fact]
    public void NeverThrowsMoreThanTheJobAllows()
    {
        DiscoveryJob job = Job(Enumerable.Range(0, 50).Select(i => Candidate($"c{i}", -40f, 0f)).ToArray());
        job.max_grenades = 30;
        var plan = new DiscoveryPlan(job);

        Run(plan, thrown => Fly(thrown, -40f, 20f));

        Assert.Equal(30, plan.Grenades);
        Assert.True(plan.Exhausted(0f));
    }

    // Out of budget, what is undecided is decided on what was thrown.
    [Fact]
    public void ClosingDecidesWhatIsLeft()
    {
        var plan = new DiscoveryPlan(Job(Candidate("hit", -40f, 0f), Candidate("unthrown", -40f, 0f)));
        DiscoveryThrow first = plan.Next(1).Single();
        plan.Observe(first, Fly(first, -40f, 20f));

        plan.Close();
        List<DiscoveryOutcome> outcomes = plan.TakeFinished();

        Assert.Equal("verified", outcomes.Single(o => o.result_id == "hit").status);
        Assert.Equal("missed", outcomes.Single(o => o.result_id == "unthrown").status);
        Assert.True(plan.Done);
    }

    // A hit whose probes were never all thrown has not been measured, and
    // must not read as perfectly forgiving.
    [Fact]
    public void AHitClosedBeforeItsProbesLandHasNoForgiveness()
    {
        var plan = new DiscoveryPlan(Job(Candidate("hit", -40f, 0f)));
        DiscoveryThrow first = plan.Next(1).Single();
        plan.Observe(first, Fly(first, -40f, 20f));
        DiscoveryThrow probe = plan.Next(1).Single();
        plan.Observe(probe, Fly(probe, -40f, 20f));

        plan.Close();
        DiscoveryOutcome outcome = plan.TakeFinished().Single();

        Assert.Equal("verified", outcome.status);
        Assert.Equal(-1f, outcome.forgiveness);
    }

    [Fact]
    public void AFullyProbedHitMeasuresItsForgiveness()
    {
        var plan = new DiscoveryPlan(Job(Candidate("hit", -40f, 0f)));
        List<DiscoveryOutcome> outcomes = Run(plan, thrown => Fly(thrown, -40f, 20f));

        DiscoveryOutcome outcome = outcomes.Single();
        Assert.Equal("verified", outcome.status);
        Assert.InRange(outcome.forgiveness, 4.9f, 10.1f);
    }

    [Theory]
    [InlineData("", "", false, "Stationary")]
    [InlineData("", "", true, "Jump")]
    [InlineData("left", "run", false, "Running")]
    [InlineData("forward", "walk", true, "WalkJump")]
    public void ReadsTheCandidatesTechnique(string direction, string gait, bool jump, string technique)
    {
        var candidate = new DiscoveryCandidate { direction = direction, gait = gait, jump_throw = jump };

        Assert.Equal(technique, DiscoveryPlan.Technique(candidate).technique);
    }
}
