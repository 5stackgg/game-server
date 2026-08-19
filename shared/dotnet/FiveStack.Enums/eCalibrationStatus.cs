namespace FiveStack.Enums;

// Whether the solver is allowed to run on this map.
//
// The solver assumes the engine, handed a recorded throw's physics seed, will
// reproduce that throw. Nothing else in the plugin depends on that being true,
// so it is checked once per map before the first solve rather than assumed.
public enum eCalibrationStatus
{
    // No verdict yet: either nothing has been attempted on this map, or the
    // launch model agreed and the live seed replay has not run.
    Unknown,

    // Nobody has thrown a grenade this session and the library has nothing
    // with a seed, so there is nothing to check against.
    NoSample,

    // The launch model does not reproduce the seed the engine recorded for a
    // real throw. Solving would still land grenades, but the aim it reported
    // back would be wrong, which is the failure a player cannot detect.
    LaunchModelMismatch,

    // The re-emitted grenade did not land where the original one did. The
    // premise of the whole solver is false on this build.
    SeedReplayMismatch,

    // The re-emitted grenade never reported a landing at all.
    SeedReplayTimedOut,

    // This runtime has no way to emit a grenade.
    Unsupported,

    Ready,
}
