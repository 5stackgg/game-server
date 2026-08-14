using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Translation;

namespace FiveStack;

// The server cannot see a webcam. Everything here is driven by the API, which
// watches the actual media and sends `camera_state <steamid,steamid>` whenever
// the set of players without a working feed changes (empty = everyone is fine).
public class CameraSystem
{
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly ILogger<CameraSystem> _logger;
    private readonly ILocalizer _localizer;

    private CancellationTokenSource? _reminderTimer;
    private HashSet<ulong> _offline = new HashSet<ulong>();
    private Guid _matchId = Guid.Empty;

    public CameraSystem(
        ILogger<CameraSystem> logger,
        GameServer gameServer,
        MatchService matchService,
        ILocalizer localizer
    )
    {
        _logger = logger;
        _gameServer = gameServer;
        _matchService = matchService;
        _localizer = localizer;
    }

    public bool IsRequired()
    {
        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();

        // The API only re-sends state when the offending set changes, so a new
        // match on this server would otherwise inherit whoever was offline when
        // the last one ended and start out paused for no reason.
        if ((matchData?.id ?? Guid.Empty) != _matchId)
        {
            _matchId = matchData?.id ?? Guid.Empty;
            Reset();
            SeedFromMatchData(matchData);
        }

        return matchData?.options.camera_required == true;
    }

    // Updates are edge-triggered, so the API stays quiet for as long as the
    // offending set holds. A plugin that lost its state mid-match -- a restart,
    // a fresh map load -- would never be told who is still offline, and would
    // let a blocked player ready up or a paused match resume. The match payload
    // carries the API's current answer, so pick it back up from there.
    private void SeedFromMatchData(MatchData? matchData)
    {
        if (matchData?.options.camera_required != true)
        {
            return;
        }

        foreach (MatchLineUp lineup in new[] { matchData.lineup_1, matchData.lineup_2 })
        {
            foreach (MatchMember member in lineup.lineup_players)
            {
                if (member.camera_ok || !ulong.TryParse(member.steam_id, out ulong steamId))
                {
                    continue;
                }

                _offline.Add(steamId);
            }
        }
    }

    public bool IsBlocking()
    {
        return IsRequired() && _offline.Count > 0;
    }

    public bool IsPlayerBlocked(IPlayer? player)
    {
        if (player == null || !player.IsValid || player.IsFakeClient || !IsRequired())
        {
            return false;
        }

        return _offline.Contains(player.SteamID);
    }

    public string OfflineNames()
    {
        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();

        if (matchData == null)
        {
            return string.Join(", ", _offline);
        }

        List<string> names = new List<string>();

        foreach (ulong steamId in _offline)
        {
            MatchMember? member = MatchUtility.GetMemberFromLineup(
                matchData,
                steamId.ToString(),
                string.Empty
            );

            names.Add(member?.name ?? steamId.ToString());
        }

        return string.Join(", ", names);
    }

    // Called from the `camera_state` console command. The payload is the full
    // set every time, never a delta, so a dropped message self-corrects on the
    // next change rather than leaving the server permanently out of sync.
    public void UpdateState(string payload)
    {
        IsRequired();

        HashSet<ulong> offline = ParseSteamIds(payload);

        if (offline.SetEquals(_offline))
        {
            return;
        }

        _offline = offline;

        _logger.LogInformation(
            $"camera state updated: {(_offline.Count == 0 ? "all clear" : OfflineNames())}"
        );

        UpdateScoreboardTags();

        if (_offline.Count > 0)
        {
            OnCamerasLost();
            return;
        }

        OnCamerasRestored();
    }

    // RCON hands us whatever was typed. Anything that is not a steam id is
    // dropped rather than throwing: a malformed message must not take the
    // camera system down mid-match.
    public static HashSet<ulong> ParseSteamIds(string payload)
    {
        HashSet<ulong> steamIds = new HashSet<ulong>();

        if (string.IsNullOrWhiteSpace(payload))
        {
            return steamIds;
        }

        foreach (
            string entry in payload.Split(
                new[] { ',', ' ' },
                StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            if (ulong.TryParse(entry.Trim(), out ulong steamId) && steamId > 0)
            {
                steamIds.Add(steamId);
            }
        }

        return steamIds;
    }

    public void Reset()
    {
        _offline.Clear();
        TimerUtility.Kill(_reminderTimer);
        _reminderTimer = null;
    }

    private void OnCamerasLost()
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null)
        {
            return;
        }

        string message = _localizer["camera.paused", OfflineNames()];

        // Gated on play actually being stopped rather than on whether anyone
        // was already offline: an organizer can resume over a breach, and the
        // next player to lose their camera has to stop play again.
        if (!match.IsPaused())
        {
            match.PauseMatch(message);
        }
        else
        {
            _gameServer.Message(MessageType.Alert, message);
        }

        TimerUtility.Kill(_reminderTimer);
        _reminderTimer = TimerUtility.Repeat(
            10,
            () =>
            {
                if (_offline.Count == 0)
                {
                    return;
                }

                _gameServer.Message(MessageType.Alert, _localizer["camera.waiting", OfflineNames()]);
            }
        );
    }

    // Deliberately does not resume: whoever is at the keyboard decides when
    // play restarts, the same as any other technical pause.
    private void OnCamerasRestored()
    {
        TimerUtility.Kill(_reminderTimer);
        _reminderTimer = null;

        _gameServer.Message(
            MessageType.Alert,
            _localizer["camera.restored", CommandUtility.PublicChatTrigger]
        );
    }

    // Reuses the ready-system convention: the scoreboard is the one place every
    // player already looks to see who is holding things up.
    private void UpdateScoreboardTags()
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (match == null || matchData == null)
        {
            return;
        }

        foreach (IPlayer player in MatchUtility.Players())
        {
            MatchMember? member = MatchUtility.GetMemberFromLineup(
                matchData,
                player.SteamID.ToString(),
                player.Name
            );

            if (member == null)
            {
                continue;
            }

            string? tag = _offline.Contains(player.SteamID)
                ? _localizer["camera.tag"]
                : null;

            match.UpdatePlayerName(player, member.name, tag);
        }
    }
}
