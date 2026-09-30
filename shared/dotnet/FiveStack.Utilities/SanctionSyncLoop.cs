using FiveStack.Entities.PlayerManagement;

namespace FiveStack.Utilities;

// On its own timer, not a game one: a community server hibernates when empty,
// which stops every frame-driven timer, and this sync is also the heartbeat the
// panel reads to show the plugin as active. A frame timer also replays every
// second it missed on waking, which would hammer the panel.
public sealed class SanctionSyncLoop : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(1);

    private readonly SanctionBook _book;
    private readonly SanctionsClient _client;
    private readonly Func<PlayerManagementSettings> _settings;
    private readonly string _version;
    private readonly string _runtime;
    private readonly Action<string> _warn;
    private readonly Action<string> _info;

    private readonly object _lock = new();
    private List<string> _present = new();
    private bool _syncing;
    private bool _requested;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _lastSyncAt;
    private string? _lastError;
    private Timer? _timer;

    public SanctionSyncLoop(
        SanctionBook book,
        SanctionsClient client,
        Func<PlayerManagementSettings> settings,
        string version,
        string runtime,
        Action<string> warn,
        Action<string> info
    )
    {
        _book = book;
        _client = client;
        _settings = settings;
        _version = version;
        _runtime = runtime;
        _warn = warn;
        _info = info;
    }

    public void Start()
    {
        _timer = new Timer(_ => _ = Tick(DateTimeOffset.UtcNow), null, TimeSpan.Zero, Poll);
    }

    public void Dispose()
    {
        Timer? timer = _timer;
        _timer = null;
        timer?.Dispose();
    }

    // Only the game thread can read the player list, so it hands it over here.
    public void Observe(IEnumerable<string> present)
    {
        List<string> snapshot = present.Distinct().ToList();

        lock (_lock)
        {
            _present = snapshot;
        }
    }

    // A sync already in flight predates whatever asked for this one, so the
    // ask is kept and served by the next sync rather than dropped.
    public void Request()
    {
        lock (_lock)
        {
            _requested = true;
        }

        try
        {
            _timer?.Change(TimeSpan.Zero, Poll);
        }
        catch (ObjectDisposedException)
        {
            // Unloading.
        }
    }

    public (DateTimeOffset? LastSyncAt, string? LastError) Status()
    {
        lock (_lock)
        {
            return (_lastSyncAt, _lastError);
        }
    }

    public async Task Tick(DateTimeOffset now)
    {
        List<string> queried;

        lock (_lock)
        {
            bool due = _requested || _startedAt == null || now - _startedAt >= Interval;

            if (_syncing || !due)
            {
                return;
            }

            _syncing = true;
            _requested = false;
            _startedAt = now;
            queried = _present.Concat(_book.Awaiting()).Distinct().ToList();
        }

        try
        {
            PlayerManagementSettings settings = _settings();

            if (!settings.IsConnected())
            {
                _book.Unanswered(queried);
                return;
            }

            SanctionSync result = await _client.Sync(
                settings,
                new PlayerSanctionsRequest
                {
                    steam_ids = queried,
                    plugin_version = _version,
                    plugin_runtime = _runtime,
                }
            );

            if (result.Sanctions == null)
            {
                Failed(queried, result.Error ?? "unknown error");
                return;
            }

            _book.Record(queried, result.Sanctions);

            bool recovered;

            lock (_lock)
            {
                recovered = _lastError != null;
                _lastError = null;
                _lastSyncAt = now;
            }

            if (recovered)
            {
                _info("sanction sync recovered");
            }
        }
        catch (Exception error)
        {
            Failed(queried, error.Message);
        }
        finally
        {
            lock (_lock)
            {
                _syncing = false;
            }
        }
    }

    private void Failed(List<string> queried, string error)
    {
        _book.Unanswered(queried);

        bool changed;

        lock (_lock)
        {
            changed = _lastError != error;
            _lastError = error;
        }

        if (changed)
        {
            _warn($"unable to sync sanctions: {error}");
        }
    }
}
