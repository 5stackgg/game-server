namespace FiveStack.Utilities;

// Holds a boolean still until it has meant it for a while.
//
// The guidance panel moves house when the player steps onto the lineup's spot,
// and "on the spot" is a distance test with a hard edge. Standing near that edge
// -- which is exactly where somebody lining a throw up stands -- flips it many
// times a second, and the panel teleports between the top of the screen and the
// bottom on every flip. The reading is correct each time; it is the acting on it
// immediately that is wrong.
public readonly struct HudSteady
{
    private HudSteady(bool committed, bool pending, int since)
    {
        Committed = committed;
        Pending = pending;
        Since = since;
    }

    /// <summary>The value callers should act on.</summary>
    public bool Committed { get; }

    private bool Pending { get; }

    private int Since { get; }

    public static HudSteady Start(bool value)
    {
        return new HudSteady(value, value, 0);
    }

    /// <summary>
    /// Feeds a fresh reading. The committed value only follows once the new
    /// reading has held for <paramref name="holdTicks" /> without going back.
    /// </summary>
    public HudSteady Read(bool reading, int now, int holdTicks)
    {
        if (reading == Committed)
        {
            // Back to where it was: whatever it was about to become is dropped,
            // so a flicker never accumulates towards a move.
            return new HudSteady(Committed, Committed, now);
        }

        if (reading != Pending)
        {
            return new HudSteady(Committed, reading, now);
        }

        return now - Since >= holdTicks
            ? new HudSteady(reading, reading, now)
            : new HudSteady(Committed, reading, Since);
    }
}
