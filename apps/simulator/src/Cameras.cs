namespace FiveStack.Simulator;

// CameraSystem: the players the api last reported without a working camera.
// It lasts the match, not the map: the api only reports a change, so a camera
// lost after a map's last round still holds the next map.
public sealed class Cameras
{
    private readonly object _gate = new();
    private readonly HashSet<string> _offline = new();
    private TaskCompletionSource? _back;
    private TaskCompletionSource? _lost;

    public bool AnyOffline
    {
        get
        {
            lock (_gate)
            {
                return _offline.Count > 0;
            }
        }
    }

    // CameraSystem.UpdateState: the api sends the full set every time it
    // changes.
    public void Update(string payload)
    {
        lock (_gate)
        {
            _offline.Clear();
            foreach (
                string steamId in payload.Split(
                    [',', ' '],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            )
            {
                if (ulong.TryParse(steamId, out ulong parsed) && parsed > 0)
                {
                    _offline.Add(steamId);
                }
            }

            Log.Info(
                _offline.Count == 0
                    ? "cameras: all clear"
                    : $"cameras offline: {string.Join(", ", _offline)}"
            );

            Settle();
        }
    }

    // CameraSystem.Reset: a new match starts with nobody reported.
    public void Clear()
    {
        lock (_gate)
        {
            _offline.Clear();
            Settle();
        }
    }

    public async Task WaitUntilBack(CancellationToken token)
    {
        while (true)
        {
            Task back;
            lock (_gate)
            {
                if (_offline.Count == 0)
                {
                    return;
                }
                _back ??= Signal();
                back = _back.Task;
            }
            await back.WaitAsync(token);
        }
    }

    public async Task<bool> WaitUntilLost(TimeSpan timeout, CancellationToken token)
    {
        Task lost;
        lock (_gate)
        {
            if (_offline.Count > 0)
            {
                return true;
            }
            _lost ??= Signal();
            lost = _lost.Task;
        }

        try
        {
            await lost.WaitAsync(timeout, token);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private void Settle()
    {
        if (_offline.Count == 0)
        {
            _back?.TrySetResult();
            _back = null;
            return;
        }

        _lost?.TrySetResult();
        _lost = null;
    }

    private static TaskCompletionSource Signal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
