namespace FiveStack.Enums;

// How a solve ended. Everything except Converged is a failure that has to be
// said out loud: a solver that quietly returns its best miss is a solver that
// saves lineups nobody can throw.
public enum eSolveOutcome
{
    Running,

    // A candidate landed inside the requested tolerance.
    Converged,

    // A whole refinement pass moved the best landing less than it costs to
    // keep going. The map geometry does not admit a throw to that point from
    // there, or not one this search can find.
    NoProgress,

    // Ran out of grenades before converging.
    GrenadeCap,

    // Ran out of wall clock before converging.
    TimedOut,

    // Nothing was worth throwing: no cleared strength, or the target is the
    // throwing position.
    NoCandidates,

    // Refused before a single grenade was emitted.
    Refused,
}
