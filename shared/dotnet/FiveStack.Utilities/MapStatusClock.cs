namespace FiveStack.Utilities;

// When a map status was decided. A status is resent until the api acknowledges
// it, in no particular order, so the api keeps only the newest by this time.
// To the millisecond, which the api compares exactly, and always after the last
// one, so two statuses in the same millisecond still keep their order.
public class MapStatusClock
{
    private readonly object _gate = new();
    private DateTime _last = DateTime.MinValue;

    public DateTime Next(DateTime utcNow)
    {
        DateTime now = new(
            utcNow.Ticks - utcNow.Ticks % TimeSpan.TicksPerMillisecond,
            DateTimeKind.Utc
        );

        lock (_gate)
        {
            _last = now > _last ? now : _last.AddMilliseconds(1);
            return _last;
        }
    }
}
