namespace FiveStack.Enums;

// Ordered by what ends it. A whole roster or a declined restore ends the first
// two on their own, so a later pause for a later reason takes over the match's
// pause, and never the other way round.
public enum ePauseReason
{
    Roster,

    Restore,

    Camera,

    Tech,

    Admin,
}
