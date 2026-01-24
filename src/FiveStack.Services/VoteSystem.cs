using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace FiveStack;

public class VoteSystem
{
    private readonly ILogger<VoteSystem> _logger;
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly CaptainSystem _captainSystem;
    private readonly IStringLocalizer _localizer;
    private readonly IMatchUtilityService _matchUtilityService;

    private CsTeam[]? _allowedTeamsToVote;
    private Action? _voteFailedCallback;
    private Action? _voteSuccessCallback;
    private Dictionary<ulong, bool> _votes = new Dictionary<ulong, bool>();

    private bool _captainOnly;
    private string? _voteMessage;
    private Timer? _voteTimeoutTimer;
    private Timer? _playerMessageTimer;
    private DateTime? _voteStartTime;
    private float? _voteTimeout;

    public VoteSystem(
        ILogger<VoteSystem> logger,
        MatchService matchService,
        GameServer gameServer,
        CaptainSystem captainSystem,
        IStringLocalizer localizer
    )
    {
        _logger = logger;
        _matchService = matchService;
        _gameServer = gameServer;
        _captainSystem = captainSystem;
        _localizer = localizer;
    }

    // TODO - either all players , or one team
    public void StartVote(
        string voteMessage,
        CsTeam[] allowedTeamsToVote,
        Action voteSuccessCallback,
        Action voteFailedCallback,
        bool captainOnly = false,
        float? timeout = null
    )
    {
        _voteMessage = voteMessage;
        _allowedTeamsToVote = allowedTeamsToVote;
        _voteSuccessCallback = voteSuccessCallback;
        _voteFailedCallback = voteFailedCallback;
        _captainOnly = captainOnly;
        _votes.Clear();
        _voteStartTime = DateTime.Now;
        _voteTimeout = timeout;

        KillTimers();

        _logger.LogInformation($"Starting vote: {voteMessage}");

        _playerMessageTimer = TimerUtility.AddTimer(
            1,
            () =>
            {
                SendVoteMessage();
            },
            TimerFlags.REPEAT
        );

        if (timeout != null)
        {
            _voteTimeoutTimer = TimerUtility.AddTimer(
                (float)timeout,
                () =>
                {
                    CheckVotes(true);
                }
            );
        }

        SendVoteMessage();
    }

    public void CancelVote()
    {
        _voteStartTime = null;
        _voteTimeout = null;
        KillTimers();
        _logger.LogInformation($"Vote Cancelled: {_voteMessage}");
        _voteFailedCallback?.Invoke();
    }

    private void VoteFailed()
    {
        _logger.LogInformation($"Vote Failed: {_voteMessage}");
        _voteStartTime = null;
        _voteTimeout = null;
        KillTimers();
        _votes.Clear();

        if (_voteFailedCallback == null)
        {
            return;
        }

        _gameServer.Message(
            HudDestination.Center,
            _localizer["vote.failed", _voteMessage ?? string.Empty]
        );
        _voteFailedCallback();
    }

    private void VoteSuccess()
    {
        _logger.LogInformation($"Vote Success: {_voteMessage}");
        _voteStartTime = null;
        _voteTimeout = null;
        KillTimers();
        _votes.Clear();

        if (_voteSuccessCallback == null)
        {
            return;
        }

        _gameServer.Message(
            HudDestination.Center,
            _localizer["vote.success", _voteMessage ?? string.Empty]
        );
        _voteSuccessCallback();
    }

    public void KillTimers()
    {
        _voteTimeoutTimer?.Kill();
        _voteTimeoutTimer = null;
        _playerMessageTimer?.Kill();
        _playerMessageTimer = null;
    }

    public bool IsVoteActive()
    {
        return _playerMessageTimer != null;
    }

    public void CastVote(CCSPlayerController player, bool vote)
    {
        if (!CanVote(player))
        {
            return;
        }

        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();

        if (matchData == null)
        {
            return;
        }

        if (IsCaptainVoteOnly())
        {
            if (
                MatchUtility
                    .GetMemberFromLineup(matchData, player.SteamID.ToString(), player.PlayerName)
                    ?.captain == false
            )
            {
                _gameServer.Message(
                    HudDestination.Chat,
                    $" {ChatColors.Red}you are not allowed to vote",
                    player
                );
                return;
            }
        }

        _votes[player.SteamID] = vote;

        SendVoteMessage();

        CheckVotes();
    }

    private void SendVoteMessage()
    {
        if (_playerMessageTimer == null)
        {
            return;
        }

        bool isCaptainVoteOnly = IsCaptainVoteOnly();

        foreach (var player in _matchUtilityService.Players())
        {
            if (isCaptainVoteOnly && _captainSystem.IsCaptain(player, player.Team) == false)
            {
                continue;
            }

            string action = _voteMessage?.ToLower() ?? "";
            int remainingSeconds = GetRemainingSeconds();

            if (!CanVote(player))
            {
                if (remainingSeconds > 0)
                {
                    player.PrintToCenter(
                        _localizer["vote.other_team_timer", action, remainingSeconds]
                    );
                }
                else
                {
                    player.PrintToCenter(_localizer["vote.other_team", action]);
                }

                continue;
            }

            if (_votes.ContainsKey(player.SteamID))
            {
                if (remainingSeconds > 0)
                {
                    player.PrintToCenter(
                        _localizer["vote.prompt_count_timer", action, remainingSeconds]
                    );
                }
                else
                {
                    player.PrintToCenter(_localizer["vote.prompt_count", action]);
                }
                continue;
            }

            if (remainingSeconds > 0)
            {
                player.PrintToCenter(
                    _localizer[
                        "vote.prompt_options_timer",
                        action,
                        CommandUtility.PublicChatTrigger,
                        CommandUtility.PublicChatTrigger,
                        remainingSeconds
                    ]
                );
            }
            else
            {
                player.PrintToCenter(
                    _localizer[
                        "vote.prompt_options",
                        action,
                        CommandUtility.PublicChatTrigger,
                        CommandUtility.PublicChatTrigger
                    ]
                );
            }
        }
    }

    public void RemovePlayerVote(ulong steamId)
    {
        if (_votes.ContainsKey(steamId))
        {
            _votes.Remove(steamId);
            CheckVotes();
        }
    }

    private bool IsCaptainVoteOnly()
    {
        var captains = GetCaptains();
        if (
            _captainOnly
            && captains[CsTeam.CounterTerrorist] != null
            && captains[CsTeam.Terrorist] != null
        )
        {
            return true;
        }

        return false;
    }

    private Dictionary<CsTeam, CCSPlayerController?> GetCaptains()
    {
        return _captainSystem.GetCaptains();
    }

    private void CheckVotes(bool fail = false)
    {
        if (_playerMessageTimer == null)
        {
            return;
        }

        int expectedVoteCount = GetExpectedVoteCount();

        if (expectedVoteCount == 0)
        {
            VoteFailed();
            return;
        }

        int totalYesVotes = _votes.Count(pair =>
        {
            return pair.Value == true;
        });

        int totalNoVotes = _votes.Count - totalYesVotes;

        if (IsCaptainVoteOnly())
        {
            if (_votes.Count < 2)
            {
                if (fail)
                {
                    VoteFailed();
                }
                return;
            }

            if (totalYesVotes >= 2)
            {
                VoteSuccess();
                return;
            }

            VoteFailed();
            return;
        }

        if (totalYesVotes >= Math.Floor(expectedVoteCount / 2.0) + 1)
        {
            VoteSuccess();
            return;
        }

        if (fail || _votes.Count >= expectedVoteCount)
        {
            VoteFailed();
        }
    }

    private bool CanVote(CCSPlayerController player)
    {
        if (_allowedTeamsToVote == null)
        {
            return true;
        }

        return _allowedTeamsToVote.Contains(player.Team);
    }

    private int GetExpectedVoteCount()
    {
        var players = MatchUtility
            .Players()
            .Where(player =>
            {
                return CanVote(player);
                ;
            })
            .ToList();

        if (IsCaptainVoteOnly())
        {
            return Math.Min(players.Count, 2);
        }

        return players.Count;
    }

    private int GetRemainingSeconds()
    {
        if (
            _voteStartTime == null
            || _voteTimeoutTimer == null
            || _playerMessageTimer == null
            || _voteTimeout == null
        )
        {
            return 0;
        }

        var elapsed = (DateTime.Now - _voteStartTime.Value).TotalSeconds;
        var remaining = (int)Math.Ceiling(_voteTimeout.Value - elapsed);
        return Math.Max(0, remaining);
    }
}
