using System.Globalization;
using FiveStack.Entities.PlayerManagement;
using FiveStack.Enums;

namespace FiveStack.Utilities;

public sealed record SanctionState(PlayerSanction? Ban, PlayerSanction? Mute, PlayerSanction? Gag)
{
    public static readonly SanctionState None = new(null, null, null);

    public bool IsBanned => Ban != null;
    public bool IsMuted => Mute != null;
    public bool IsGagged => Gag != null;

    public static eSanctionChange Changes(SanctionState previous, SanctionState next)
    {
        eSanctionChange changes = eSanctionChange.None;

        if (next.IsBanned)
        {
            changes |= eSanctionChange.Banned;
        }

        if (next.IsMuted && !previous.IsMuted)
        {
            changes |= eSanctionChange.Muted;
        }
        else if (!next.IsMuted && previous.IsMuted)
        {
            changes |= eSanctionChange.Unmuted;
        }

        if (next.IsGagged && !previous.IsGagged)
        {
            changes |= eSanctionChange.Gagged;
        }
        else if (!next.IsGagged && previous.IsGagged)
        {
            changes |= eSanctionChange.Ungagged;
        }

        return changes;
    }
}

public class SanctionBook
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<PlayerSanction>> _bySteamId = new();

    // The panel answers for exactly the players it was asked about, so a
    // player it was asked about and says nothing of has been cleared.
    public void Record(IEnumerable<string> queried, IEnumerable<PlayerSanction> sanctions)
    {
        lock (_lock)
        {
            foreach (string steamId in queried)
            {
                _bySteamId.Remove(steamId);
            }

            foreach (PlayerSanction sanction in sanctions)
            {
                if (string.IsNullOrEmpty(sanction.steam_id))
                {
                    continue;
                }

                if (!_bySteamId.TryGetValue(sanction.steam_id, out List<PlayerSanction>? list))
                {
                    list = new List<PlayerSanction>();
                    _bySteamId[sanction.steam_id] = list;
                }

                list.Add(sanction);
            }
        }
    }

    public void Forget(string steamId)
    {
        lock (_lock)
        {
            _bySteamId.Remove(steamId);
        }
    }

    public SanctionState StateFor(string steamId, DateTimeOffset now)
    {
        List<PlayerSanction> active;

        lock (_lock)
        {
            if (!_bySteamId.TryGetValue(steamId, out List<PlayerSanction>? sanctions))
            {
                return SanctionState.None;
            }

            active = sanctions
                .Where(sanction => sanction.expires_at == null || sanction.expires_at > now)
                .ToList();
        }

        return new SanctionState(
            Strongest(active.Where(sanction => sanction.type == "ban")),
            Strongest(active.Where(sanction => sanction.type is "mute" or "silence")),
            Strongest(active.Where(sanction => sanction.type is "gag" or "silence"))
        );
    }

    public static string Until(PlayerSanction sanction)
    {
        return sanction.expires_at == null
            ? ""
            : sanction.expires_at.Value.UtcDateTime.ToString(
                "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture
            ) + " UTC";
    }

    public static string Reason(PlayerSanction sanction)
    {
        return ChatUtility.StripFormatting(sanction.reason ?? "").Trim();
    }

    public static string KickReason(PlayerSanction ban)
    {
        string reason = Reason(ban);

        return reason.Length == 0 ? "Banned" : $"Banned: {reason}";
    }

    // A permanent sanction outlasts any timed one, and of two timed ones the
    // later expiry is the one the player is actually serving.
    private static PlayerSanction? Strongest(IEnumerable<PlayerSanction> sanctions)
    {
        return sanctions
            .OrderByDescending(sanction => sanction.expires_at == null)
            .ThenByDescending(sanction => sanction.expires_at)
            .FirstOrDefault();
    }
}
