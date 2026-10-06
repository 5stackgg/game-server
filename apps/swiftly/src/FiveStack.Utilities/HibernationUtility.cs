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

    private static ISwiftlyCore _core = null!;
    private static EventDelegates.OnTick? _tickHandler;
    private static long _lastTickMs;

    public static void Initialize(ISwiftlyCore core)
    {
        _core = core;
        _tickHandler = () => Volatile.Write(ref _lastTickMs, Environment.TickCount64);
        _core.Event.OnTick += _tickHandler;
    }

    public static void Shutdown()
    {
        if (_tickHandler != null)
        {
            _core.Event.OnTick -= _tickHandler;
            _tickHandler = null;
        }
    }

    public static void NextTick(Action callback)
    {
        if (Environment.TickCount64 - Volatile.Read(ref _lastTickMs) < TickingWindowMs)
        {
            _core.Scheduler.NextTick(callback);
            return;
        }

        _core.Scheduler.NextWorldUpdate(callback);
    }
}
