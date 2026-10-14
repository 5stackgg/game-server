using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

namespace FiveStack.Tests;

public class PracticeThrowKeysTests
{
    private const uint Forward = 1 << 3;
    private const uint Left = 1 << 9;
    private const uint Walk = 1 << 16;

    private static LineupRecord Lineup(
        eThrowTechnique technique,
        eThrowStrength? strength = null,
        bool jumpThrow = false
    )
    {
        return new LineupRecord
        {
            technique = technique.ToString(),
            strength = strength?.ToString(),
            release = new ThrowSnapshot { jump_throw = jumpThrow },
        };
    }

    [Theory]
    [InlineData(null, true, false)]
    [InlineData(eThrowStrength.Full, true, false)]
    [InlineData(eThrowStrength.Half, true, true)]
    [InlineData(eThrowStrength.Drop, false, true)]
    public void StrengthIsWhichMouseButtons(eThrowStrength? strength, bool left, bool right)
    {
        ThrowKeys keys = PracticeThrowKeys.For(Lineup(eThrowTechnique.Stationary, strength));

        Assert.Equal(left, keys.LeftClick);
        Assert.Equal(right, keys.RightClick);
    }

    [Fact]
    public void AStandingThrowAsksForNoKeys()
    {
        ThrowKeys keys = PracticeThrowKeys.For(Lineup(eThrowTechnique.Stationary));

        Assert.Equal("Stand still", keys.Word);
        Assert.False(keys.Jump);
        Assert.False(keys.Crouch);
        Assert.False(keys.Walk);
        Assert.Empty(keys.Move);
        Assert.Equal("", keys.RunUp);
    }

    // A lineup recorded before the technique was classified still knows it was
    // thrown on the bind.
    [Fact]
    public void TheJumpThrowBindIsAJumpWhateverTheTechniqueSays()
    {
        ThrowKeys keys = PracticeThrowKeys.For(Lineup(eThrowTechnique.Stationary, jumpThrow: true));

        Assert.True(keys.Jump);
        Assert.Equal("Jump throw", keys.Word);
        Assert.Equal("jump", keys.Figure);
    }

    [Theory]
    [InlineData(eThrowTechnique.Stationary, false, "stationary")]
    [InlineData(eThrowTechnique.Walking, true, "walkjump")]
    [InlineData(eThrowTechnique.Running, false, "running")]
    [InlineData(eThrowTechnique.Crouch, true, "crouchjump")]
    [InlineData(eThrowTechnique.RunJump, false, "runjump")]
    public void TheFigureIsTheTechniqueInTheAirWhenItJumps(
        eThrowTechnique technique,
        bool jumpThrow,
        string figure
    )
    {
        Assert.Equal(figure, PracticeThrowKeys.For(Lineup(technique, jumpThrow: jumpThrow)).Figure);
    }

    [Fact]
    public void AMovingThrowWithNoRunUpStillMovesForward()
    {
        ThrowKeys keys = PracticeThrowKeys.For(Lineup(eThrowTechnique.WalkJump));

        Assert.Equal(new[] { "W" }, keys.Move);
        Assert.True(keys.Walk);
        Assert.True(keys.Jump);
        Assert.Equal("Walk + jump throw", keys.Word);
    }

    [Fact]
    public void ARecordedRunUpSaysWhichKeys()
    {
        LineupRecord lineup = Lineup(eThrowTechnique.Walking);

        lineup.approach = new[] { -600, -400, -200, 0 }
            .Select(t => new ApproachSample
            {
                t = t,
                buttons = Forward | Left | Walk,
                on_ground = true,
                pos = new Vec3(t, 0f, 0f),
            })
            .ToList();

        ThrowKeys keys = PracticeThrowKeys.For(lineup);

        Assert.Equal(new[] { "W", "A" }, keys.Move);
        Assert.True(keys.Walk);
        Assert.Equal("Hold Shift + W + A 0.6s first", keys.RunUp);
    }

    [Fact]
    public void EveryMoveKeyHasAKeycap()
    {
        foreach (string key in PracticeThrowKeys.MoveKeys)
        {
            Assert.Contains(key.ToLowerInvariant(), HudSlots.ThrowKeycaps);
        }
    }
}
