namespace FiveStack.Utilities;

// Whether a surface can still be used on the layout entity in hand. A respawned
// entity can be given the index the old one freed, and what was sent to the old
// one is gone with it, so the index alone does not say the client holds anything.
public static class HudLayoutIdentity
{
    public static bool Same(object held, uint heldIndex, object current, uint currentIndex)
    {
        return heldIndex == currentIndex && ReferenceEquals(held, current);
    }
}
