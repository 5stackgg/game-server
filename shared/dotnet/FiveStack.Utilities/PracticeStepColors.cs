namespace FiveStack.Utilities;

/// <summary>
/// Distinct colours for the throws of one execute, so a player can tell which
/// of them is theirs and which of the smokes in the air is the one they threw.
///
/// An execute puts four or five grenades up at once from four or five spots.
/// Drawn in the utility's own colour they are all the same white, so the only
/// way to know which one landed where is to have watched it the whole way --
/// which is exactly what a player rehearsing a lineup cannot do. A colour per
/// step turns "the smoke" into "the cyan one", which is sayable in comms and
/// readable on the ground afterwards.
///
/// Ordinal, not random: the same execute has to come up the same way twice, or
/// the colour is one more thing to relearn every run.
/// </summary>
public static class PracticeStepColors
{
    public readonly struct StepColor
    {
        public StepColor(string name, byte r, byte g, byte b)
        {
            Name = name;
            R = r;
            G = g;
            B = b;
        }

        /// <summary>What to call it in chat. The colour is only useful if it is sayable.</summary>
        public string Name { get; }

        public byte R { get; }
        public byte G { get; }
        public byte B { get; }
    }

    // Deliberately no green and no red: those two are the aim reticle's ramp,
    // where green means inside tolerance and red means far off it. A step
    // wearing either would be answering a question it was not asked.
    private static readonly StepColor[] Palette = new[]
    {
        new StepColor("cyan", 0, 220, 255),
        new StepColor("yellow", 255, 225, 60),
        new StepColor("magenta", 255, 90, 220),
        new StepColor("blue", 90, 130, 255),
        new StepColor("orange", 255, 150, 40),
        new StepColor("purple", 185, 120, 255),
        new StepColor("white", 240, 240, 240),
        new StepColor("pink", 255, 150, 190),
    };

    public static int Count => Palette.Length;

    /// <summary>
    /// The colour for a position in the execute. Wraps rather than running out:
    /// an execute longer than the palette repeats a colour, which is worse than
    /// distinct but far better than an unlit marker.
    /// </summary>
    public static StepColor For(int index)
    {
        if (index < 0)
        {
            index = 0;
        }

        return Palette[index % Palette.Length];
    }
}
