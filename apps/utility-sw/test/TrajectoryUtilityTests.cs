using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

public class TrajectoryUtilityTests
{
    private static ThrowSnapshot Release(
        float speed = 0f,
        bool onGround = true,
        bool ducked = false,
        bool walking = false,
        bool jumpThrow = false,
        float velocityZ = 0f
    )
    {
        return new ThrowSnapshot
        {
            speed = speed,
            on_ground = onGround,
            ducked = ducked,
            walking = walking,
            jump_throw = jumpThrow,
            velocity = new Vec3(speed, 0f, velocityZ),
        };
    }

    [Theory]
    [InlineData(1.0f, eThrowStrength.Full)]
    [InlineData(0.75f, eThrowStrength.Full)]
    [InlineData(0.5f, eThrowStrength.Half)]
    [InlineData(0.25f, eThrowStrength.Half)]
    [InlineData(0.0f, eThrowStrength.Drop)]
    public void ClassifiesTheThreeReleaseStrengths(float raw, eThrowStrength expected)
    {
        Assert.Equal(expected, TrajectoryUtility.ClassifyStrength(raw));
    }

    [Fact]
    public void StandingStillIsStationary()
    {
        Assert.Equal(
            eThrowTechnique.Stationary,
            TrajectoryUtility.ClassifyTechnique(Release())
        );
    }

    [Fact]
    public void WalkSpeedIsWalkingAndAboveItIsRunning()
    {
        Assert.Equal(
            eThrowTechnique.Walking,
            TrajectoryUtility.ClassifyTechnique(Release(speed: 120f))
        );
        Assert.Equal(
            eThrowTechnique.Running,
            TrajectoryUtility.ClassifyTechnique(Release(speed: 240f))
        );
    }

    [Fact]
    public void HoldingWalkIsWalkingEvenAtRunSpeed()
    {
        Assert.Equal(
            eThrowTechnique.Walking,
            TrajectoryUtility.ClassifyTechnique(Release(speed: 240f, walking: true))
        );
    }

    // A hand-timed jump throw does not always set m_bJumpThrow, so leaving the
    // ground has to count on its own or half of all lineups misclassify.
    [Fact]
    public void LeavingTheGroundCountsAsAJumpWithoutTheFlag()
    {
        Assert.Equal(
            eThrowTechnique.Jump,
            TrajectoryUtility.ClassifyTechnique(Release(onGround: false))
        );
        Assert.Equal(
            eThrowTechnique.Jump,
            TrajectoryUtility.ClassifyTechnique(Release(velocityZ: 200f))
        );
    }

    [Fact]
    public void JumpComposesWithMovementAndStance()
    {
        Assert.Equal(
            eThrowTechnique.RunJump,
            TrajectoryUtility.ClassifyTechnique(Release(speed: 240f, jumpThrow: true))
        );
        Assert.Equal(
            eThrowTechnique.WalkJump,
            TrajectoryUtility.ClassifyTechnique(Release(speed: 100f, jumpThrow: true))
        );
        Assert.Equal(
            eThrowTechnique.CrouchJump,
            TrajectoryUtility.ClassifyTechnique(Release(jumpThrow: true, ducked: true))
        );
        Assert.Equal(
            eThrowTechnique.Crouch,
            TrajectoryUtility.ClassifyTechnique(Release(ducked: true))
        );
    }

    [Fact]
    public void DerivesAnglesFromAVelocityVector()
    {
        var (pitch, yaw) = TrajectoryUtility.AnglesFromVelocity(new Vec3(100f, 0f, 0f));
        Assert.Equal(0f, yaw, 3);
        Assert.Equal(0f, pitch, 3);

        (pitch, yaw) = TrajectoryUtility.AnglesFromVelocity(new Vec3(0f, 100f, 0f));
        Assert.Equal(90f, yaw, 3);

        // Up is negative pitch in the engine's convention.
        (pitch, yaw) = TrajectoryUtility.AnglesFromVelocity(new Vec3(0f, 0f, 100f));
        Assert.Equal(-90f, pitch, 3);
    }

    [Fact]
    public void AnglesFromAZeroVectorDoNotProduceNaN()
    {
        var (pitch, yaw) = TrajectoryUtility.AnglesFromVelocity(new Vec3(0f, 0f, 0f));
        Assert.False(float.IsNaN(pitch));
        Assert.False(float.IsNaN(yaw));
    }

    private static TrajectoryPoint Point(float x, float y, float z, int t, bool bounce = false)
    {
        return new TrajectoryPoint
        {
            p = new Vec3(x, y, z),
            t = t,
            bounce = bounce,
        };
    }

    [Fact]
    public void SimplifyCollapsesAStraightRun()
    {
        var points = new List<TrajectoryPoint>();
        for (int i = 0; i <= 20; i++)
        {
            points.Add(Point(i * 10f, 0f, 0f, i));
        }

        var simplified = TrajectoryUtility.Simplify(points);

        Assert.Equal(2, simplified.Count);
        Assert.Equal(0f, simplified[0].p.x);
        Assert.Equal(200f, simplified[^1].p.x);
    }

    [Fact]
    public void SimplifyKeepsTheShapeOfACurve()
    {
        var points = new List<TrajectoryPoint>();
        for (int i = 0; i <= 40; i++)
        {
            float x = i * 10f;
            points.Add(Point(x, 0f, -(x * x) / 400f, i));
        }

        var simplified = TrajectoryUtility.Simplify(points);

        Assert.True(simplified.Count > 2, "an arc must not collapse to a line");
        Assert.True(
            simplified.Count < points.Count,
            "an arc should still compact substantially"
        );
    }

    // A bounce is where the path changes direction. Dropping one is how a
    // replayed line ends up going through a wall.
    [Fact]
    public void SimplifyNeverDropsABounce()
    {
        var points = new List<TrajectoryPoint>();
        for (int i = 0; i <= 10; i++)
        {
            points.Add(Point(i * 10f, 0f, 0f, i));
        }
        points[5].bounce = true;
        for (int i = 11; i <= 20; i++)
        {
            points.Add(Point(100f, (i - 10) * 10f, 0f, i));
        }

        var simplified = TrajectoryUtility.Simplify(points);

        Assert.Contains(simplified, p => p.bounce);
        Assert.Equal(1, simplified.Count(p => p.bounce));
    }

    [Fact]
    public void SimplifyPassesThroughShortPaths()
    {
        var points = new List<TrajectoryPoint> { Point(0f, 0f, 0f, 0), Point(10f, 0f, 0f, 1) };
        Assert.Equal(2, TrajectoryUtility.Simplify(points).Count);
        Assert.Empty(TrajectoryUtility.Simplify(new List<TrajectoryPoint>()));
    }
}
