using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

public class PracticeSolverUtilityTests
{
    private static readonly Vec3 Eye = new Vec3(0f, 0f, 64f);
    private static readonly Vec3 Target = new Vec3(1000f, 0f, 0f);

    private static SolveRequest Request(params string[] strengths)
    {
        return PracticeSolverUtility.Defaults(
            new SolveRequest
            {
                map = "de_mirage",
                utility_type = "Smoke",
                target = Target,
                eye = Eye,
                feet = new Vec3(0f, 0f, 0f),
                strengths = strengths.Length == 0
                    ? new List<string> { nameof(eThrowStrength.Full) }
                    : strengths.ToList(),
            }
        );
    }

    private static CalibrationReport Calibration(float correction = 1f)
    {
        return new CalibrationReport
        {
            map = "de_mirage",
            status = nameof(eCalibrationStatus.Ready),
            speed_corrections = new Dictionary<string, float>
            {
                { nameof(eThrowStrength.Full), correction },
            },
        };
    }

    [Fact]
    public void DefaultsFillInAndClamp()
    {
        SolveRequest request = PracticeSolverUtility.Defaults(new SolveRequest());

        Assert.Equal(PracticeSolverUtility.DefaultTolerance, request.tolerance);
        Assert.Equal(PracticeSolverUtility.DefaultBatchSize, request.batch_size);
        Assert.Equal(PracticeSolverUtility.DefaultMaxGrenades, request.max_grenades);

        SolveRequest silly = PracticeSolverUtility.Defaults(
            new SolveRequest
            {
                tolerance = 5000f,
                batch_size = 900,
                max_grenades = 100000,
                max_seconds = 99999f,
            }
        );

        Assert.Equal(PracticeSolverUtility.MaxTolerance, silly.tolerance);
        Assert.Equal(PracticeSolverUtility.MaxBatchSize, silly.batch_size);
        Assert.Equal(PracticeSolverUtility.MaxGrenadeCap, silly.max_grenades);
        Assert.Equal(PracticeSolverUtility.MaxSecondsCap, silly.max_seconds);
    }

    // A cap below a batch would emit nothing at all.
    [Fact]
    public void TheGrenadeCapNeverFallsBelowOneBatch()
    {
        SolveRequest request = PracticeSolverUtility.Defaults(
            new SolveRequest { batch_size = 20, max_grenades = 3 }
        );

        Assert.Equal(20, request.max_grenades);
    }

    [Fact]
    public void NoClearedStrengthMeansNothingToThrow()
    {
        SolveRequest request = PracticeSolverUtility.Defaults(
            new SolveRequest { target = Target, eye = Eye }
        );

        Assert.Empty(PracticeSolverUtility.CoarseSweep(request));
    }

    [Fact]
    public void ATargetUnderfootIsNotAThrow()
    {
        SolveRequest request = Request();
        request.target = new Vec3(Eye.x + 4f, Eye.y, Eye.z);

        Assert.Empty(PracticeSolverUtility.CoarseSweep(request));
    }

    [Fact]
    public void TheSweepStartsOnTheDirectBearing()
    {
        List<SolveCandidate> sweep = PracticeSolverUtility.CoarseSweep(Request());

        Assert.NotEmpty(sweep);
        Assert.Equal(
            PracticeLaunchUtility.BearingTo(Eye, Target),
            sweep[0].yaw,
            3
        );
    }

    // Truncating the sweep to fit the budget has to drop the least likely
    // throws, not an arbitrary corner of the grid.
    [Fact]
    public void TheSweepIsOrderedByHowLikelyAThrowIs()
    {
        List<SolveCandidate> sweep = PracticeSolverUtility.CoarseSweep(Request());
        float bearing = PracticeLaunchUtility.BearingTo(Eye, Target);

        float previous = 0f;

        foreach (SolveCandidate candidate in sweep)
        {
            float offset = MathF.Abs(
                PracticeLaunchUtility.NormalizeYaw(candidate.yaw - bearing)
            );

            Assert.True(offset >= previous - 0.001f);
            previous = offset;
        }
    }

    [Fact]
    public void EveryClearedStrengthIsSwept()
    {
        List<SolveCandidate> sweep = PracticeSolverUtility.CoarseSweep(
            Request(
                nameof(eThrowStrength.Full),
                nameof(eThrowStrength.Half),
                nameof(eThrowStrength.Drop)
            )
        );

        Assert.Equal(
            3,
            sweep.Select(candidate => candidate.strength_bucket).Distinct().Count()
        );
    }

    [Fact]
    public void NeighboursAreTheEightAround()
    {
        var centre = new SolveCandidate
        {
            pitch = -10f,
            yaw = 30f,
            strength = 1f,
            strength_bucket = nameof(eThrowStrength.Full),
        };

        List<SolveCandidate> neighbours = PracticeSolverUtility.Neighbours(centre, 3f);

        Assert.Equal(8, neighbours.Count);
        Assert.DoesNotContain(
            neighbours,
            candidate =>
                MathF.Abs(candidate.pitch - centre.pitch) < 0.001f
                && MathF.Abs(candidate.yaw - centre.yaw) < 0.001f
        );
        Assert.All(
            neighbours,
            candidate => Assert.Equal(centre.strength_bucket, candidate.strength_bucket)
        );
    }

    [Fact]
    public void NeighboursStayInsideALegalPitch()
    {
        var steep = new SolveCandidate
        {
            pitch = -88f,
            yaw = 0f,
            strength_bucket = nameof(eThrowStrength.Full),
        };

        Assert.All(
            PracticeSolverUtility.Neighbours(steep, 10f),
            candidate => Assert.True(candidate.pitch >= -89f && candidate.pitch <= 89f)
        );
    }

    // Two aims a degree apart thrown at different strengths are different
    // throws, not neighbours, so refining one says nothing about the other.
    [Fact]
    public void DifferentStrengthsAreNeverTheSameBasin()
    {
        var full = new SolveCandidate
        {
            pitch = 0f,
            yaw = 0f,
            strength_bucket = nameof(eThrowStrength.Full),
        };
        var half = new SolveCandidate
        {
            pitch = 0f,
            yaw = 0f,
            strength_bucket = nameof(eThrowStrength.Half),
        };

        Assert.Equal(float.MaxValue, PracticeSolverUtility.Separation(full, half));
        Assert.Equal(0f, PracticeSolverUtility.Separation(full, full));
    }

    [Fact]
    public void RefinementPicksSeparatedBasinsNotNeighbours()
    {
        var observations = new List<SolveObservation>
        {
            Observation(0f, 0f, 10f),
            Observation(0.5f, 0.5f, 11f),
            Observation(30f, 0f, 40f),
            Observation(-30f, 0f, 50f),
        };

        List<SolveObservation> picked = PracticeSolverUtility.PickDistinct(
            observations,
            4,
            PracticeSolverUtility.MinSeparationDegrees
        );

        Assert.Equal(3, picked.Count);
        Assert.Equal(10f, picked[0].distance);
        Assert.DoesNotContain(picked, observation => observation.distance == 11f);
    }

    [Fact]
    public void AGrenadeThatNeverLandedCanNeverWin()
    {
        var lost = Observation(0f, 0f, 1f);
        lost.landed = false;

        List<SolveObservation> picked = PracticeSolverUtility.PickDistinct(
            new[] { lost, Observation(40f, 0f, 900f) },
            4,
            PracticeSolverUtility.MinSeparationDegrees
        );

        Assert.Single(picked);
        Assert.Equal(900f, picked[0].distance);
    }

    [Fact]
    public void NearIdenticalAimsShareAKey()
    {
        var first = new SolveCandidate
        {
            pitch = 10f,
            yaw = 20f,
            strength_bucket = nameof(eThrowStrength.Full),
        };
        var second = new SolveCandidate
        {
            pitch = 10.001f,
            yaw = 20.001f,
            strength_bucket = nameof(eThrowStrength.Full),
        };
        var apart = new SolveCandidate
        {
            pitch = 10.5f,
            yaw = 20f,
            strength_bucket = nameof(eThrowStrength.Full),
        };

        Assert.Equal(
            PracticeSolverUtility.CandidateKey(first),
            PracticeSolverUtility.CandidateKey(second)
        );
        Assert.NotEqual(
            PracticeSolverUtility.CandidateKey(first),
            PracticeSolverUtility.CandidateKey(apart)
        );
    }

    [Fact]
    public void TheMeasuredSpeedCorrectionReachesTheThrow()
    {
        SolveRequest request = Request();
        var candidate = new SolveCandidate
        {
            pitch = -10f,
            yaw = 0f,
            strength = 1f,
            strength_bucket = nameof(eThrowStrength.Full),
        };

        LaunchSeed plain = PracticeSolverUtility.SeedFor(request, candidate, Calibration());
        LaunchSeed corrected = PracticeSolverUtility.SeedFor(
            request,
            candidate,
            Calibration(1.2f)
        );

        Assert.Equal(plain.speed * 1.2f, corrected.speed, 2);
    }

    // The seed is the point of a solve: without it the lineup is a suggestion,
    // with it the plugin can throw the winning grenade again exactly.
    [Fact]
    public void TheWinningThrowBecomesAReplayableLineup()
    {
        SolveRequest request = Request();
        request.name = "window";
        request.requested_by = "76561198000000001";

        var best = new SolveObservation
        {
            candidate = new SolveCandidate
            {
                pitch = -12.5f,
                yaw = 3.5f,
                strength = 1f,
                strength_bucket = nameof(eThrowStrength.Full),
            },
            landing = new Vec3(1002f, 3f, 0f),
            distance = 3.6f,
            landed = true,
            bounces = 2,
        };

        LineupRecord lineup = PracticeSolverUtility.ToLineup(
            request,
            best,
            Calibration(),
            "swiftlys2",
            "1.2.3"
        );

        Assert.True(lineup.HasPhysicsSeed());
        Assert.True(lineup.IsExactlyReplayable());
        Assert.Equal("window", lineup.name);
        Assert.Equal("de_mirage", lineup.map);
        Assert.Equal(nameof(eThrowTechnique.Stationary), lineup.technique);
        Assert.Equal(nameof(eThrowStrength.Full), lineup.strength);
        Assert.Equal(-12.5f, lineup.release.pitch, 3);
        Assert.Equal(3.5f, lineup.release.yaw, 3);
        Assert.Equal(2, lineup.bounces);
        Assert.Equal(best.landing.x, lineup.detonation_position.x, 3);
        Assert.Equal("swiftlys2", lineup.plugin_runtime);
    }

    // A solved throw is stationary by construction, so a release snapshot that
    // said otherwise would send a player somewhere they cannot reproduce it.
    [Fact]
    public void TheSolvedReleaseIsAStandingThrow()
    {
        LineupRecord lineup = PracticeSolverUtility.ToLineup(
            Request(),
            new SolveObservation
            {
                candidate = new SolveCandidate
                {
                    strength = 1f,
                    strength_bucket = nameof(eThrowStrength.Full),
                },
                landed = true,
            },
            Calibration(),
            "swiftlys2",
            ""
        );

        Assert.True(lineup.release.on_ground);
        Assert.False(lineup.release.jump_throw);
        Assert.False(lineup.release.ducked);
        Assert.Equal(0f, lineup.release.speed);
    }

    // The re-throw is the difference between a measurement and a coincidence,
    // so a confirmation that did not land is a failure and not a shrug.
    [Fact]
    public void AConfirmationHasToLandAndBeClose()
    {
        SolveRequest request = Request();
        request.tolerance = 40f;

        Assert.True(
            PracticeSolverUtility.Confirms(
                new SolveObservation { landed = true, distance = 39f },
                request
            )
        );
        Assert.False(
            PracticeSolverUtility.Confirms(
                new SolveObservation { landed = true, distance = 41f },
                request
            )
        );
        Assert.False(
            PracticeSolverUtility.Confirms(
                new SolveObservation { landed = false, distance = 1f },
                request
            )
        );
        Assert.False(PracticeSolverUtility.Confirms(new SolveObservation(), request));
    }

    [Fact]
    public void ParsesTheRconForm()
    {
        Assert.True(
            PracticeSolverUtility.TryParse(
                new[]
                {
                    "target=1000,-250.5,64",
                    "from=0,0,0",
                    "utility=HE",
                    "tolerance=25",
                    "grenades=80",
                    "seconds=30",
                    "steam=76561198000000001",
                    "name=window smoke",
                },
                out SolveRequest request,
                out string error
            )
        );

        Assert.Equal("", error);
        Assert.Equal(1000f, request.target.x, 3);
        Assert.Equal(-250.5f, request.target.y, 3);
        Assert.Equal("HighExplosive", request.utility_type);
        Assert.Equal(25f, request.tolerance, 3);
        Assert.Equal(80, request.max_grenades);
        Assert.Equal("window smoke", request.name);
        Assert.Equal("76561198000000001", request.requested_by);
    }

    // from= is a floor position, because that is what a player reads off the
    // map; the throw itself comes out of the eyes.
    [Fact]
    public void AGivenThrowingPositionStandsUp()
    {
        PracticeSolverUtility.TryParse(
            new[] { "target=500,0,0", "from=10,20,30" },
            out SolveRequest request,
            out _
        );

        Assert.Equal(30f, request.feet.z, 3);
        Assert.Equal(
            30f + PracticeSolverUtility.StandingEyeHeight,
            request.eye.z,
            3
        );
    }

    [Fact]
    public void RefusesACallWithNoTarget()
    {
        Assert.False(
            PracticeSolverUtility.TryParse(
                new[] { "utility=Smoke" },
                out _,
                out string error
            )
        );

        Assert.Contains("target=x,y,z is required", error);
    }

    [Fact]
    public void RefusesAMalformedPoint()
    {
        Assert.False(
            PracticeSolverUtility.TryParse(new[] { "target=1000,64" }, out _, out string error)
        );

        Assert.Contains("target must be x,y,z", error);
    }

    // A positional argument list over RCON is a solve for the wrong point that
    // nobody notices, so anything that is not key=value is an error.
    [Fact]
    public void RefusesPositionalArguments()
    {
        Assert.False(
            PracticeSolverUtility.TryParse(
                new[] { "1000", "0", "64" },
                out _,
                out string error
            )
        );

        Assert.Contains("every argument is key=value", error);
    }

    [Fact]
    public void RefusesAnUnknownArgument()
    {
        Assert.False(
            PracticeSolverUtility.TryParse(new[] { "target=1,2,3", "wind=5" }, out _, out string error)
        );

        Assert.Contains("unknown argument", error);
    }

    private static SolveObservation Observation(float yaw, float pitch, float distance)
    {
        return new SolveObservation
        {
            candidate = new SolveCandidate
            {
                pitch = pitch,
                yaw = yaw,
                strength = 1f,
                strength_bucket = nameof(eThrowStrength.Full),
            },
            distance = distance,
            landed = true,
        };
    }
}
