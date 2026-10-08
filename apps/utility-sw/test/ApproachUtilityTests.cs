using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Xunit;

public class ApproachUtilityTests
{
    private const uint InJump = 1 << 1;
    private const uint InMoveRight = 1 << 10;

    private static ApproachFrame Frame(
        int tick,
        float x = 0f,
        float vx = 0f,
        bool onGround = true,
        uint buttons = 0
    )
    {
        return new ApproachFrame(
            tick,
            new ApproachSample
            {
                pos = new Vec3(x, 0f, 0f),
                vel = new Vec3(vx, 0f, 0f),
                pitch = -10f,
                yaw = 90f,
                buttons = buttons,
                on_ground = onGround,
            }
        );
    }

    private static List<ApproachFrame> Run(int from, int to)
    {
        var frames = new List<ApproachFrame>();

        for (int tick = from; tick <= to; tick++)
        {
            frames.Add(Frame(tick, x: tick, vx: 250f, buttons: InMoveRight));
        }

        return frames;
    }

    [Fact]
    public void AThrowMadeStandingStillHasNoRunUp()
    {
        Assert.Empty(ApproachUtility.Cut(Run(100, 120), 120, 120));
    }

    [Fact]
    public void TheRunUpStartsAtTheLastStandstillAndEndsAtTheRelease()
    {
        List<ApproachSample> approach = ApproachUtility.Cut(Run(100, 120), 105, 120);

        Assert.Equal(16, approach.Count);
        Assert.Equal(105f, approach[0].pos.x);
        Assert.Equal(120f, approach[^1].pos.x);
        Assert.Equal(0, approach[^1].t);
    }

    [Fact]
    public void TimesAreMillisecondsBeforeTheRelease()
    {
        List<ApproachSample> approach = ApproachUtility.Cut(Run(100, 120), 118, 120);

        Assert.Equal(new[] { -31, -16, 0 }, approach.Select(sample => sample.t));
    }

    [Fact]
    public void NothingAfterTheReleaseIsPartOfIt()
    {
        List<ApproachSample> approach = ApproachUtility.Cut(Run(100, 130), 110, 120);

        Assert.Equal(120f, approach[^1].pos.x);
        Assert.All(approach, sample => Assert.True(sample.t <= 0));
    }

    [Fact]
    public void NoStandstillMeansTheWholeBufferWasRunUp()
    {
        Assert.Equal(21, ApproachUtility.Cut(Run(100, 120), null, 120).Count);
    }

    [Fact]
    public void AStandstillOlderThanTheBufferMeansTheWholeBufferWasRunUp()
    {
        Assert.Equal(21, ApproachUtility.Cut(Run(100, 120), 10, 120).Count);
    }

    [Fact]
    public void ItNeverRunsPastTheCap()
    {
        List<ApproachSample> approach = ApproachUtility.Cut(Run(1, 300), null, 300);

        Assert.Equal(ApproachUtility.MaxSamples, approach.Count);
        Assert.Equal(0, approach[^1].t);
        Assert.Equal(300f - ApproachUtility.MaxSamples + 1, approach[0].pos.x);
    }

    [Fact]
    public void TheBufferKeepsOnlyTheNewestFrames()
    {
        var buffer = new Queue<ApproachFrame>();

        foreach (ApproachFrame frame in Run(1, 200))
        {
            ApproachUtility.Push(buffer, frame.Tick, frame.Sample);
        }

        Assert.Equal(ApproachUtility.MaxSamples, buffer.Count);
        Assert.Equal(200 - ApproachUtility.MaxSamples + 1, buffer.Peek().Tick);
    }

    [Fact]
    public void AJumpThrowFromAStandstillKeepsTheJump()
    {
        var frames = new List<ApproachFrame>
        {
            Frame(99),
            Frame(100),
            Frame(101, vx: 0f, onGround: false, buttons: InJump),
        };

        List<ApproachSample> approach = ApproachUtility.Cut(frames, 100, 101);

        Assert.Equal(2, approach.Count);
        Assert.True(approach[0].on_ground);
        Assert.False(approach[1].on_ground);
        Assert.Equal(InJump, approach[1].buttons & InJump);
    }

    [Fact]
    public void ARunUpDoesNotStartBeforeATeleport()
    {
        List<ApproachFrame> frames = Run(100, 110);
        frames.AddRange(
            Run(111, 120).Select(frame =>
                Frame(frame.Tick, x: 5000f + frame.Tick, vx: 250f)
            )
        );

        List<ApproachSample> approach = ApproachUtility.Cut(frames, null, 120);

        Assert.Equal(10, approach.Count);
        Assert.Equal(5111f, approach[0].pos.x);
    }

    // System.Text.Json refuses to write a NaN, which would lose the lineup
    // along with its run-up.
    [Fact]
    public void ANonFiniteSampleCostsTheRunUpNotTheSave()
    {
        List<ApproachFrame> frames = Run(100, 120);
        frames[10] = Frame(110, x: float.NaN);

        Assert.Empty(ApproachUtility.Cut(frames, null, 120));
    }

    [Fact]
    public void TheReleaseAloneIsNoRunUp()
    {
        Assert.Empty(ApproachUtility.Cut(new[] { Frame(120) }, null, 120));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, -16)]
    [InlineData(-2, -31)]
    [InlineData(-64, -1000)]
    [InlineData(-127, -1984)]
    public void TicksBecomeMilliseconds(int ticks, int expected)
    {
        Assert.Equal(expected, ApproachUtility.Milliseconds(ticks));
    }
}
