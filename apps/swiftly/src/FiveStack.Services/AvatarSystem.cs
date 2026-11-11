using System.Collections.Concurrent;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.StringTable;

namespace FiveStack;

// Shows each player's team roster photo as their avatar in game. CS2 reads
// ServerAvatarOverrides (SteamID64 -> PNG) when sv_reliableavatardata is on,
// which the match cfgs set. The table is rebuilt on every map load, and every
// map load sets the match up again.
public class AvatarSystem
{
    public const string TableName = "ServerAvatarOverrides";
    public const int MaxBytes = 16 * 1024;
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(10);

    // BotHider never puts a photo at index 0 and reserves it with an empty row.
    private const string ReservedKey = "__5stack_no_avatar__";

    private static readonly byte[] PngSignature =
    [
        0x89,
        0x50,
        0x4E,
        0x47,
        0x0D,
        0x0A,
        0x1A,
        0x0A,
    ];

    private readonly ISwiftlyCore _core;
    private readonly EnvironmentService _environmentService;
    private readonly ILogger<AvatarSystem> _logger;

    private readonly ConcurrentDictionary<string, byte[]> _downloaded = new();
    private readonly HashSet<string> _written = new();
    private int _generation;

    public AvatarSystem(
        ILogger<AvatarSystem> logger,
        ISwiftlyCore core,
        EnvironmentService environmentService
    )
    {
        _logger = logger;
        _core = core;
        _environmentService = environmentService;
    }

    public static Dictionary<string, string> Wanted(MatchData match)
    {
        Dictionary<string, string> wanted = new();

        foreach (MatchLineUp lineup in new[] { match.lineup_1, match.lineup_2 })
        {
            foreach (MatchMember member in lineup?.lineup_players ?? new List<MatchMember>())
            {
                if (
                    string.IsNullOrEmpty(member.steam_id)
                    || !ulong.TryParse(member.steam_id, out _)
                    || string.IsNullOrEmpty(member.avatar_url)
                    || !member.avatar_url.StartsWith('/')
                )
                {
                    continue;
                }

                wanted[member.steam_id] = member.avatar_url;
            }
        }

        return wanted;
    }

    public static bool IsUsablePng(byte[] image)
    {
        return image.Length > PngSignature.Length
            && image.Length <= MaxBytes
            && image.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature);
    }

    public async void Apply(MatchData match)
    {
        if (_environmentService.IsOfflineMode())
        {
            return;
        }

        int generation = Interlocked.Increment(ref _generation);
        Dictionary<string, string> wanted = Wanted(match);
        Dictionary<string, byte[]> images = new();

        foreach (string path in _downloaded.Keys.Except(wanted.Values).ToList())
        {
            _downloaded.TryRemove(path, out _);
        }

        string[] paths = wanted.Values.Distinct().ToArray();
        byte[]?[] downloads = await Task.WhenAll(paths.Select(Download));
        Dictionary<string, byte[]?> byPath = paths
            .Zip(downloads)
            .ToDictionary(pair => pair.First, pair => pair.Second);

        foreach ((string steamId, string path) in wanted)
        {
            if (byPath[path] is byte[] image)
            {
                images[steamId] = image;
            }
        }

        HibernationUtility.NextTick(() =>
        {
            if (generation != Volatile.Read(ref _generation))
            {
                return;
            }

            Write(images);
        });
    }

    public void Clear()
    {
        int generation = Interlocked.Increment(ref _generation);

        HibernationUtility.NextTick(() =>
        {
            if (generation != Volatile.Read(ref _generation))
            {
                return;
            }

            Write(new Dictionary<string, byte[]>());
        });
    }

    private async Task<byte[]?> Download(string path)
    {
        if (_downloaded.TryGetValue(path, out byte[]? cached))
        {
            return cached;
        }

        try
        {
            using CancellationTokenSource timeout = new(DownloadTimeout);
            byte[] image = await HttpClientProvider.Client.GetByteArrayAsync(
                $"{_environmentService.GetApiUrl()}{path}",
                timeout.Token
            );

            if (!IsUsablePng(image))
            {
                _logger.LogWarning($"avatar {path} is not a PNG of at most {MaxBytes} bytes");
                return null;
            }

            _downloaded[path] = image;
            return image;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"unable to download avatar {path}: {ex.Message}");
            return null;
        }
    }

    private void Write(Dictionary<string, byte[]> images)
    {
        try
        {
            IStringTable? table = _core.StringTable.FindTable(TableName);
            if (table == null)
            {
                _logger.LogWarning($"{TableName} string table not found, avatars stay as they are");
                return;
            }

            if (images.Count > 0 && table.NumStrings == 0)
            {
                table.SetOrAddStringUserData(
                    ReservedKey,
                    StringTableUserData.FromRaw(Array.Empty<byte>())
                );
            }

            foreach (string steamId in _written.Except(images.Keys).ToList())
            {
                if (table.FindStringIndex(steamId) is int index)
                {
                    table.SetStringUserData(
                        index,
                        StringTableUserData.FromRaw(Array.Empty<byte>()),
                        true
                    );
                }

                _written.Remove(steamId);
            }

            int changed = 0;
            foreach ((string steamId, byte[] image) in images)
            {
                _written.Add(steamId);

                if (
                    table.TryGetStringUserData(steamId, out StringTableOutUserData existing)
                    && existing.AsRaw().SequenceEqual(image)
                )
                {
                    continue;
                }

                table.SetOrAddStringUserData(steamId, StringTableUserData.FromRaw(image), true);
                changed++;
            }

            if (changed > 0)
            {
                _logger.LogInformation($"set {changed} roster photo avatars");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"unable to write {TableName}: {ex.Message}");
        }
    }
}
