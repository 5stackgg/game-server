using CounterStrikeSharp.API;

namespace FiveStack.Utilities;

// A hibernating server stops running frames but keeps updating the world, and
// it only wakes once somebody is let in. Whatever has to be in place before
// that (the match, its password, the map) is queued through here: on the next
// frame while the server is awake, exactly as before, and on the next world
// update while it hibernates.
public static class HibernationUtility
{
    private static volatile bool _hibernating;

    public static void SetHibernating(bool hibernating)
    {
        _hibernating = hibernating;
    }

    public static void NextFrame(Action callback)
    {
        if (_hibernating)
        {
            Server.NextWorldUpdate(callback);
            return;
        }

        Server.NextFrame(callback);
    }
}
