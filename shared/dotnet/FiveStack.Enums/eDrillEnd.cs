namespace FiveStack.Enums;

// How a run ended. The two failures are said out loud rather than left as a
// drill that quietly stops advancing.
public enum eDrillEnd
{
    Running,

    // Every lineup in the queue was thrown, skipped or dropped.
    Completed,

    // The player asked it to stop.
    Stopped,

    // The panel stopped answering, so throws stopped being scored.
    Unscorable,

    // Lineup after lineup could not be stood on.
    Unloadable,

    // The player left or the map changed. Nobody is there to be told, so this
    // one is never summarised.
    Abandoned,
}
