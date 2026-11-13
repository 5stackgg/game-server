namespace FiveStack.Utilities;

// Match fetches answer in any order, and taking an older answer after a newer
// one would hand a bound seat back to its placeholder. One lineup refresh runs at
// a time, an ask while it runs is kept for one more after it, and an answer to a
// request older than the last one taken is dropped.
public class FetchSequence
{
    private readonly object _lock = new();
    private bool _running;
    private bool _again;
    private int _requested;
    private int _taken;

    public bool TryStart()
    {
        lock (_lock)
        {
            if (_running)
            {
                _again = true;
                return false;
            }

            _running = true;
            return true;
        }
    }

    public bool Finish()
    {
        lock (_lock)
        {
            bool again = _again;
            _running = false;
            _again = false;
            return again;
        }
    }

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
