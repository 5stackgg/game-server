using System.Collections.Concurrent;
using FiveStack.Utilities;
using Xunit;

// The api's ack is read on the websocket thread, so it can land while the
// retry timer is still sending that same message.
public class PendingMessageUtilityTests
{
    private static readonly DateTime SentAt = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ResentAt = SentAt.AddSeconds(10);

    private static ConcurrentDictionary<Guid, (string Event, DateTime Timestamp)> Pending(
        Guid messageId
    )
    {
        ConcurrentDictionary<Guid, (string Event, DateTime Timestamp)> pending = new();
        pending[messageId] = ("round_end", SentAt);

        return pending;
    }

    [Fact]
    public void AMessageAckedWhileItWasBeingResentStaysAcked()
    {
        Guid messageId = Guid.NewGuid();
        var pending = Pending(messageId);
        var resending = pending.ToList().Single();

        pending.TryRemove(messageId, out _);
        PendingMessageUtility.MarkResent(pending, resending, ResentAt);

        Assert.False(pending.ContainsKey(messageId));
    }

    [Fact]
    public void AMessageStillUnackedWaitsAgainFromItsResend()
    {
        Guid messageId = Guid.NewGuid();
        var pending = Pending(messageId);
        var resending = pending.ToList().Single();

        PendingMessageUtility.MarkResent(pending, resending, ResentAt);

        Assert.Equal(("round_end", ResentAt), pending[messageId]);
    }
}
