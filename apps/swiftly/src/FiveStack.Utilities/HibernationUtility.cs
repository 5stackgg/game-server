using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;

namespace FiveStack.Utilities;

// A hibernating server stops ticking but keeps updating the world, and it only
// wakes once somebody is let in. Whatever has to be in place before that (the
// match, its password, the map) is queued through here: on the next tick while
// the server is ticking, exactly as before, and on the next world update when
// it is not.
public static class HibernationUtility
{
    private const long TickingWindowMs = 250;
    private const long HibernatingAfterMs = 5000;

    private static ISwiftlyCore _core = null!;
    private static EventDelegates.OnTick? _tickHandler;
    private static EventDelegates.OnWorldUpdate? _worldUpdateHandler;
    private static long _lastTickMs;
    private static long _updatingWithoutTicksSinceMs;

    public static void Initialize(ISwiftlyCore core)
    {
        _core = core;
        _tickHandler = () => Volatile.Write(ref _lastTickMs, Environment.TickCount64);
        _core.Event.OnTick += _tickHandler;

        _worldUpdateHandler = OnWorldUpdate;
        _core.Event.OnWorldUpdate += _worldUpdateHandler;
    }

    public static void Shutdown()
    {
        if (_tickHandler != null)
        {
            _core.Event.OnTick -= _tickHandler;
            _tickHandler = null;
        }

        if (_worldUpdateHandler != null)
        {
            _core.Event.OnWorldUpdate -= _worldUpdateHandler;
            _worldUpdateHandler = null;
        }
    }

    private static bool IsTicking =>
        Environment.TickCount64 - Volatile.Read(ref _lastTickMs) < TickingWindowMs;

    // A map load stops ticks and world updates together and plugin load starts
    // with neither seen, so only world updates that keep arriving with no tick
    // between them say the server is hibernating.
    private static void OnWorldUpdate()
    {
        if (IsTicking)
        {
            Volatile.Write(ref _updatingWithoutTicksSinceMs, 0);
        }
        else if (Volatile.Read(ref _updatingWithoutTicksSinceMs) == 0)
        {
            Volatile.Write(ref _updatingWithoutTicksSinceMs, Environment.TickCount64);
        }
    }

    public static bool IsHibernating
    {
        get
        {
            long since = Volatile.Read(ref _updatingWithoutTicksSinceMs);

            return since != 0 && Environment.TickCount64 - since > HibernatingAfterMs;
        }
    }

    public static void NextTick(Action callback)
    {
        if (IsTicking)
        {
            _core.Scheduler.NextTick(callback);
            return;
        }

        _core.Scheduler.NextWorldUpdate(callback);
    }
}
