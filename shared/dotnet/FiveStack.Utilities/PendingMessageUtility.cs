using System.Collections.Concurrent;

namespace FiveStack.Utilities;

public static class PendingMessageUtility
{
    // The websocket thread drops a message the moment the api acks it, which
    // can be while the retry timer is still sending it. Only a message still
    // pending as it was read may be put back, or the acked event goes again.
    public static void MarkResent<TMessage>(
        ConcurrentDictionary<Guid, (TMessage Event, DateTime Timestamp)> pending,
        KeyValuePair<Guid, (TMessage Event, DateTime Timestamp)> resent,
        DateTime resentAt
    )
    {
        pending.TryUpdate(resent.Key, (resent.Value.Event, resentAt), resent.Value);
    }
}
