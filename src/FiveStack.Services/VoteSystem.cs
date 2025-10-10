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
    private readonly IStringLocalizer _localizer;

    private CsTeam[]? _allowedTeamsToVote;
    private Action? _voteFailedCallback;
    private Action? _voteSuccessCallback;
    private Dictionary<ulong, bool> _votes = new Dictionary<ulong, bool>();

    private bool _captainOnly;
    private string? _voteMessage;
    private Timer? _voteTimeoutTimer;
    private Timer? _playerMessageTimer;

    public VoteSystem(
        ILogger<VoteSystem> logger,
        MatchService matchService,
        GameServer gameServer,
        IStringLocalizer localizer
    )
    {
        _logger = logger;
        _matchService = matchService;
        _gameServer = gameServer;
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

        KillTimers();

        _logger.LogInformation($"Starting vote: {voteMessage}");

        _playerMessageTimer = TimerUtility.AddTimer(
            3,
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
        KillTimers();
        _logger.LogInformation($"Vote Cancelled: {_voteMessage}");
        _voteFailedCallback?.Invoke();
    }

    private void VoteFailed()
    {
        _logger.LogInformation($"Vote Failed: {_voteMessage}");
        KillTimers();

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
        KillTimers();

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
        _playerMessageTimer?.Kill();
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
        bool isCaptainVoteOnly = IsCaptainVoteOnly();
        var captains = GetCaptains();

        foreach (var player in MatchUtility.Players())
        {
            if (isCaptainVoteOnly && captains[player.Team] != player)
            {
                continue;
            }

            string action = _voteMessage?.ToLower() ?? "";

            if (!CanVote(player))
            {
                player.PrintToCenterAlert(_localizer["vote.other_team", action]);

                continue;
            }

            if (_votes.ContainsKey(player.SteamID))
            {
                player.PrintToCenterAlert(
                    _localizer["vote.prompt_count", action, _votes.Count, GetExpectedVoteCount()]
                );
                continue;
            }

            player.PrintToCenterAlert(
                _localizer[
                    "vote.prompt_options",
                    action,
                    CommandUtility.PublicChatTrigger,
                    CommandUtility.PublicChatTrigger
                ]
            );
        }
    }

    private bool IsCaptainVoteOnly()
    {
        if (
            _captainOnly
            && GetCaptains()[CsTeam.CounterTerrorist] != null
            && GetCaptains()[CsTeam.Terrorist] != null
        )
        {
            return true;
        }

        return false;
    }

    private Dictionary<CsTeam, CCSPlayerController?> GetCaptains()
    {
        var captains = new Dictionary<CsTeam, CCSPlayerController?>
        {
            { CsTeam.CounterTerrorist, null },
            { CsTeam.Terrorist, null },
        };

        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();

        if (matchData == null)
        {
            return captains;
        }

        foreach (var player in MatchUtility.Players())
        {
            if (
                MatchUtility
                    .GetMemberFromLineup(matchData, player.SteamID.ToString(), player.PlayerName)
                    ?.captain ?? false
            )
            {
                captains[player.Team] = player;
            }
        }

        return captains;
    }

    private void CheckVotes(bool fail = false)
    {
        int expectedVoteCount = GetExpectedVoteCount();

        int totalYesVotes = _votes.Count(pair =>
        {
            return pair.Value == true;
        });

        int totalNoVotes = _votes.Count - totalYesVotes;

        if (IsCaptainVoteOnly())
        {
            if (_votes.Count < 2)
            {
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
}
