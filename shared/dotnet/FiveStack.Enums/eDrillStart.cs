namespace FiveStack.Enums;

// Why a drill did or did not start. Every refusal is a named one: a .drill that
// does nothing and says nothing reads as a broken command.
public enum eDrillStart
{
    Started,

    // This player is already in a run.
    AlreadyRunning,

    // The server does not allow a lineup to teleport anybody, so there is no
    // drill to run.
    ReplayDisabled,

    // No panel, so no scoring, so no run: the whole point of a drill is the
    // number at the end.
    NotConnected,

    // The library is empty, or nothing in it can be drilled.
    NothingToDrill,
}
