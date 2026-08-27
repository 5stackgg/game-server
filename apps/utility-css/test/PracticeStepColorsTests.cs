using FiveStack.Utilities;
using Xunit;

public class PracticeStepColorsTests
{
    // The whole point is telling four smokes apart, so neighbouring steps must
    // not share a colour.
    [Fact]
    public void EveryColorInThePaletteIsDistinct()
    {
        var seen = new HashSet<(byte, byte, byte)>();

        for (int index = 0; index < PracticeStepColors.Count; index++)
        {
            PracticeStepColors.StepColor color = PracticeStepColors.For(index);

            Assert.True(
                seen.Add((color.R, color.G, color.B)),
                $"step {index} ({color.Name}) repeats a colour"
            );
        }
    }

    [Fact]
    public void EveryColorIsSayable()
    {
        for (int index = 0; index < PracticeStepColors.Count; index++)
        {
            Assert.False(string.IsNullOrWhiteSpace(PracticeStepColors.For(index).Name));
        }
    }

    // Green and red are the aim reticle's ramp: green means inside tolerance
    // and red means far off it. A step wearing either would answer a question
    // it was not asked.
    [Fact]
    public void NothingIsMistakableForTheAimRamp()
    {
        for (int index = 0; index < PracticeStepColors.Count; index++)
        {
            PracticeStepColors.StepColor color = PracticeStepColors.For(index);

            bool green = color.G > 150 && color.R < 120 && color.B < 120;
            bool red = color.R > 150 && color.G < 120 && color.B < 120;

            Assert.False(green, $"{color.Name} reads as the lined-up green");
            Assert.False(red, $"{color.Name} reads as the missed red");
        }
    }

    // An execute longer than the palette repeats rather than running out: a
    // repeated colour is worse than a distinct one and far better than an
    // unlit marker.
    [Fact]
    public void PositionsPastThePaletteWrap()
    {
        Assert.Equal(
            PracticeStepColors.For(0).Name,
            PracticeStepColors.For(PracticeStepColors.Count).Name
        );
    }

    // The same execute has to come up the same way twice, or the colour is one
    // more thing to relearn every run.
    [Fact]
    public void TheSamePositionIsAlwaysTheSameColor()
    {
        Assert.Equal(PracticeStepColors.For(3).Name, PracticeStepColors.For(3).Name);
        Assert.Equal(PracticeStepColors.For(3).R, PracticeStepColors.For(3).R);
    }

    [Fact]
    public void ANegativePositionDoesNotThrow()
    {
        Assert.Equal(PracticeStepColors.For(0).Name, PracticeStepColors.For(-1).Name);
    }
}
