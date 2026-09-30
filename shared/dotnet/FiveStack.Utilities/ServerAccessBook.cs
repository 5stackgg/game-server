using System.Runtime.InteropServices;
using FiveStack.Enums;

namespace FiveStack.Utilities;

public sealed record ServerAccessSnapshot(
    bool Loaded,
    bool Restricted,
    string? Version,
    int Allowed,
    string? Error
);

// Who may join a restricted community server, as the panel last listed it.
// The list is only ever replaced by a newer one: a panel that cannot be reached
// leaves the last list in force rather than opening the server up or locking
// everyone out, and before any list has loaded the engine decides alone.
public class ServerAccessBook
{
    private readonly object _lock = new();
    private bool _loaded;
    private bool _restricted;
    private string? _version;
    private HashSet<string> _allowed = new();
    private readonly HashSet<string> _denied = new();
    private string? _message;
    private string? _error;

    public bool IsCurrent(string version)
    {
        lock (_lock)
        {
            return _loaded && _version == version;
        }
    }

    // True when this changes what is enforced, so only a change is logged.
    public bool Load(bool restricted, string version, IEnumerable<string> steamIds)
    {
        HashSet<string> allowed = restricted
            ? steamIds
                .Where(steamId => !string.IsNullOrWhiteSpace(steamId))
                .Select(steamId => steamId.Trim())
                .ToHashSet()
            : new HashSet<string>();

        lock (_lock)
        {
            bool changed = !_loaded || _restricted != restricted || _version != version;

            _loaded = true;
            _restricted = restricted;
            _version = version;
            _allowed = allowed;
            _error = null;

            return changed;
        }
    }

    // True when the error is new, so a panel that stays down is reported once.
    public bool FetchFailed(string error)
    {
        lock (_lock)
        {
            bool changed = _error != error;
            _error = error;

            return changed;
        }
    }

    // The panel answers for exactly the players it was asked about, so one it
    // was asked about and did not deny is allowed in.
    public void Answered(IEnumerable<string> queried, IEnumerable<string> denied, string? message)
    {
        lock (_lock)
        {
            foreach (string steamId in queried)
            {
                _denied.Remove(steamId);
            }

            foreach (string steamId in denied)
            {
                if (!string.IsNullOrEmpty(steamId))
                {
                    _denied.Add(steamId);
                }
            }

            _message = message;
        }
    }

    // A denial is only good for the visit it was given on: a player removed
    // and later re-added must not be kicked on the way back in by a stale one.
    public void Left(string steamId)
    {
        lock (_lock)
        {
            _denied.Remove(steamId);
        }
    }

    public bool IsDenied(string steamId)
    {
        lock (_lock)
        {
            return _denied.Contains(steamId);
        }
    }

    public eServerAccess Decide(string steamId)
    {
        lock (_lock)
        {
            if (!_loaded)
            {
                return eServerAccess.Unknown;
            }

            if (!_restricted)
            {
                return eServerAccess.Open;
            }

            return _allowed.Contains(steamId) ? eServerAccess.Allowed : eServerAccess.Denied;
        }
    }

    public ServerAccessSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new ServerAccessSnapshot(_loaded, _restricted, _version, _allowed.Count, _error);
        }
    }

    public string KickReason(string denied)
    {
        string message;

        lock (_lock)
        {
            message = ChatUtility.StripFormatting(_message ?? "").Trim();
        }

        return message.Length == 0 ? denied : $"{denied} - {message}";
    }

    // The client's claimed steam id leads its auth ticket. Steam has not
    // verified it yet; a spoofer is caught by the sync once Steam has.
    public static unsafe ulong TicketSteamId(nint ticket, int length)
    {
        if (ticket == nint.Zero || length < 8)
        {
            return 0;
        }

        return MemoryMarshal.Read<ulong>(new ReadOnlySpan<byte>((void*)ticket, 8));
    }
}
