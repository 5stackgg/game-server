namespace FiveStack.Utilities;

// Match fetches answer in any order, and taking an older answer after a newer
// one would hand a bound seat back to its placeholder. An answer to a request
// older than the last one taken is dropped.
public class FetchSequence
{
    private readonly object _lock = new();
    private int _requested;
    private int _taken;

    public int Request()
    {
        return Interlocked.Increment(ref _requested);
    }

    public bool TryTake(int request)
    {
        lock (_lock)
        {
            if (request < _taken)
            {
                return false;
            }

            _taken = request;
            return true;
        }
    }
}
