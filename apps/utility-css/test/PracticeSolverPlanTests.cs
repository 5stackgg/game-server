using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// The search, driven against a stand-in for the server.
//
// The oracle here is deliberately not a physics model -- it is a landing
// function with the one property that matters: it is piecewise. There is a
// wall, and throws that clear it and throws that do not are unrelated. A search
// that only ever walked downhill would sit against that wall forever, which is
// why the sweep and the several-basin refinement exist and why these tests are
// worth having.
public class PracticeSolverPlanTests
{
    private static readonly Vec3 Eye = new Vec3(0f, 0f, 64f);
    private static readonly Vec3 Target = new Vec3(1200f, 0f, 0f);

    private class Oracle
    {
        public float YawStar;
        public float PitchStar;
        public float WallPitch = float.MaxValue;
        public float Floor;
        public bool Lost;
        public float Constant = -1f;

        public int Thrown;
        public readonly List<string> Keys = new List<string>();

        public virtual SolveObservation Throw(SolveCandidate candidate)
        {
            Thrown++;
            Keys.Add(PracticeSolverUtility.CandidateKey(candidate));

            if (Lost)
            {
                return new SolveObservation { candidate = candidate };
            }

            float error =
                Constant >= 0f
                    ? Constant
                    : candidate.pitch > WallPitch
                        ? 850f
                        : Floor
                            + (MathF.Abs(candidate.yaw - YawStar) * 12f)
                            + (MathF.Abs(candidate.pitch - PitchStar) * 15f);

            return new SolveObservation
            {
                candidate = candidate,
                landing = Target + new Vec3(error, 0f, 0f),
                distance = error,
                landed = true,
            };
        }
    }

    private static SolveRequest Request(float tolerance = 40f, int grenades = 300)
    {
        return new SolveRequest
        {
            map = "de_mirage",
            utility_type = "Smoke",
            target = Target,
            eye = Eye,
            feet = new Vec3(0f, 0f, 0f),
            tolerance = tolerance,
            max_grenades = grenades,
            batch_size = 20,
            max_seconds = 120f,
            strengths = new List<string> { nameof(eThrowStrength.Full) },
        };
    }

    private static SolveResult Run(
        SolveRequest request,
        Oracle oracle,
        out PracticeSolverPlan plan
    )
    {
        PracticeSolverPlan built = new PracticeSolverPlan(request);
        plan = built;

        while (true)
        {
            List<SolveCandidate> batch = built.NextBatch();

            if (batch.Count == 0)
            {
                break;
            }

            foreach (SolveCandidate candidate in batch)
            {
                built.Observe(oracle.Throw(candidate));
            }
        }

        return built.Finish(1f);
    }

    [Fact]
    public void ConvergesThroughAWall()
    {
        var oracle = new Oracle
        {
            YawStar = 2f,
            PitchStar = -20f,
            WallPitch = -5f,
        };

        SolveResult result = Run(Request(), oracle, out PracticeSolverPlan plan);

        Assert.Equal(nameof(eSolveOutcome.Converged), result.outcome);
        Assert.NotNull(result.best);
        Assert.True(result.best!.distance <= plan.Request.tolerance);
        Assert.True(result.thrown < plan.Request.max_grenades);
        // The sweep alone does not land inside tolerance here; if it ever does,
        // this test has stopped testing the refinement.
        Assert.True(result.batches > 1);
    }

    // The reason several basins are kept. The throw that looks best after the
    // sweep bottoms out above tolerance; the answer is in a basin that was
    // second at the time and would have been thrown away by anything that
    // refined only the leader.
    [Fact]
    public void TheSecondBestBasinCanStillWin()
    {
        var oracle = new SplitOracle();

        SolveResult result = Run(Request(tolerance: 40f), oracle, out _);

        Assert.Equal(nameof(eSolveOutcome.Converged), result.outcome);
        Assert.NotNull(result.best);
        Assert.True(
            MathF.Abs(result.best!.candidate.yaw - SplitOracle.FarYaw) < 8f,
            $"the winning throw came from the wrong basin: yaw {result.best.candidate.yaw}"
        );
    }

    private class SplitOracle : Oracle
    {
        public const float NearYaw = 0f;
        public const float NearPitch = -18f;
        public const float FarYaw = 22f;
        public const float FarPitch = -19.5f;

        // The near basin is smooth, obvious and never good enough.
        private const float NearFloor = 55f;

        public override SolveObservation Throw(SolveCandidate candidate)
        {
            Thrown++;
            Keys.Add(PracticeSolverUtility.CandidateKey(candidate));

            float near =
                NearFloor
                + (MathF.Abs(candidate.yaw - NearYaw) * 30f)
                + (MathF.Abs(candidate.pitch - NearPitch) * 30f);

            float far =
                (MathF.Abs(candidate.yaw - FarYaw) * 12f)
                + (MathF.Abs(candidate.pitch - FarPitch) * 40f);

            float error = MathF.Min(near, far);

            return new SolveObservation
            {
                candidate = candidate,
                landing = Target + new Vec3(error, 0f, 0f),
                distance = error,
                landed = true,
            };
        }
    }

    // A search that cannot get anywhere has to say so. Silently returning its
    // best miss is how a lineup nobody can throw ends up in a library.
    [Fact]
    public void GivesUpLoudlyWhenNothingImproves()
    {
        var oracle = new Oracle { Constant = 500f };

        SolveResult result = Run(Request(), oracle, out PracticeSolverPlan plan);

        Assert.Equal(nameof(eSolveOutcome.NoProgress), result.outcome);
        Assert.False(result.Converged());
        Assert.Contains("500", result.message);
        Assert.True(result.thrown < plan.Request.max_grenades);
    }

    [Fact]
    public void StopsAtTheGrenadeCap()
    {
        var oracle = new Oracle { Constant = 500f };
        SolveRequest request = Request(grenades: 40);

        SolveResult result = Run(request, oracle, out _);

        Assert.Equal(nameof(eSolveOutcome.GrenadeCap), result.outcome);
        Assert.Equal(40, result.thrown);
        Assert.Equal(40, oracle.Thrown);
    }

    [Fact]
    public void NeverThrowsMoreThanItWasAllowed()
    {
        foreach (int cap in new[] { 20, 45, 100, 300 })
        {
            var oracle = new Oracle { Constant = 500f };
            SolveResult result = Run(Request(grenades: cap), oracle, out _);

            Assert.True(oracle.Thrown <= cap, $"threw {oracle.Thrown} with a cap of {cap}");
            Assert.Equal(oracle.Thrown, result.thrown);
        }
    }

    [Fact]
    public void NeverThrowsTheSameAimTwice()
    {
        var oracle = new Oracle { YawStar = 40f, PitchStar = -30f };

        Run(Request(tolerance: 8f), oracle, out _);

        Assert.Equal(oracle.Keys.Count, oracle.Keys.Distinct().Count());
    }

    [Fact]
    public void BatchesAreBounded()
    {
        var plan = new PracticeSolverPlan(Request());
        var oracle = new Oracle { Constant = 500f };

        while (true)
        {
            List<SolveCandidate> batch = plan.NextBatch();

            if (batch.Count == 0)
            {
                break;
            }

            Assert.True(batch.Count <= plan.Request.batch_size);

            foreach (SolveCandidate candidate in batch)
            {
                plan.Observe(oracle.Throw(candidate));
            }
        }
    }

    [Fact]
    public void NoCalibratedStrengthMeansNoThrows()
    {
        SolveRequest request = Request();
        request.strengths = new List<string>();

        var plan = new PracticeSolverPlan(request);

        Assert.Empty(plan.NextBatch());
        Assert.Equal(nameof(eSolveOutcome.NoCandidates), plan.Finish(0f).outcome);
        Assert.Contains("no strength has been calibrated", plan.Finish(0f).message);
    }

    [Fact]
    public void ATargetUnderfootIsRefusedBeforeAnythingIsThrown()
    {
        SolveRequest request = Request();
        request.target = new Vec3(Eye.x + 5f, Eye.y, Eye.z);

        var plan = new PracticeSolverPlan(request);

        Assert.Empty(plan.NextBatch());
        Assert.Equal(nameof(eSolveOutcome.NoCandidates), plan.Finish(0f).outcome);
        Assert.Equal(0, plan.Thrown);
    }

    [Fact]
    public void GrenadesThatNeverLandedAreNotAnAnswer()
    {
        var oracle = new Oracle { Lost = true };

        SolveResult result = Run(Request(), oracle, out _);

        Assert.Null(result.best);
        Assert.Equal(nameof(eSolveOutcome.NoProgress), result.outcome);
        Assert.Contains("no grenade reported a landing", result.message);
    }

    [Fact]
    public void TheClockIsACap()
    {
        var plan = new PracticeSolverPlan(Request());
        var oracle = new Oracle { Constant = 500f };

        foreach (SolveCandidate candidate in plan.NextBatch())
        {
            plan.Observe(oracle.Throw(candidate));
        }

        Assert.False(plan.Expired(10f));
        Assert.True(plan.Expired(plan.Request.max_seconds));
        Assert.Equal(nameof(eSolveOutcome.TimedOut), plan.Finish(500f).outcome);
    }

    // A solve that already has its answer stops asking for grenades.
    [Fact]
    public void ConvergingEndsTheSearch()
    {
        var oracle = new Oracle { YawStar = 0f, PitchStar = -18f };
        var plan = new PracticeSolverPlan(Request());

        foreach (SolveCandidate candidate in plan.NextBatch())
        {
            plan.Observe(oracle.Throw(candidate));
        }

        Assert.True(plan.Converged());
        Assert.Empty(plan.NextBatch());
        Assert.Equal(nameof(eSolveOutcome.Converged), plan.Finish(1f).outcome);
    }

    [Fact]
    public void ProgressSaysWhereItIs()
    {
        var plan = new PracticeSolverPlan(Request());

        Assert.Contains("nothing landed yet", plan.Progress());
        Assert.Equal("sweep", plan.Phase);

        var oracle = new Oracle { Constant = 137f };

        foreach (SolveCandidate candidate in plan.NextBatch())
        {
            plan.Observe(oracle.Throw(candidate));
        }

        Assert.Contains("137u", plan.Progress());
        Assert.Contains("20/300", plan.Progress());
    }

    // The sweep is the part that finds basins, so it must not be allowed to eat
    // the budget the refinement needs.
    [Fact]
    public void TheSweepLeavesRoomToRefine()
    {
        var plan = new PracticeSolverPlan(Request(grenades: 100));
        var oracle = new Oracle { Constant = 500f };
        int sweepThrows = 0;

        while (plan.Phase == "sweep")
        {
            List<SolveCandidate> batch = plan.NextBatch();

            if (batch.Count == 0)
            {
                break;
            }

            sweepThrows += batch.Count;

            foreach (SolveCandidate candidate in batch)
            {
                plan.Observe(oracle.Throw(candidate));
            }
        }

        Assert.True(sweepThrows <= 60 + plan.Request.batch_size);
        Assert.True(sweepThrows > 0);
    }
}
