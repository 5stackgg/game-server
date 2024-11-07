using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace FiveStack;

public class VoteSystem
{
    private readonly ILogger<VoteSystem> _logger;
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;

    private Action? _voteFailedCallback;
    private Action? _voteSuccessCallback;
    private Dictionary<ulong, bool> _votes = new Dictionary<ulong, bool>();

    private bool _captainOnly;
    private string? _voteMessage;
    private Timer? _voteTimeoutTimer;
    private Timer? _playerMessageTimer;

    public VoteSystem(ILogger<VoteSystem> logger, MatchService matchService, GameServer gameServer)
    {
        _logger = logger;
        _matchService = matchService;
        _gameServer = gameServer;
    }

    // TODO - either all players , or one team
    public void StartVote(
        string voteMessage,
        Action voteSuccessCallback,
        Action voteFailedCallback,
        bool captainOnly = false,
        float? timeout = null
    )
    {
        _voteMessage = voteMessage;
        _captainOnly = captainOnly;
        _voteSuccessCallback = voteSuccessCallback;
        _voteFailedCallback = voteFailedCallback;

        KillTimers();

        _logger.LogInformation($"Starting Vote: {voteMessage}");

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
        _logger.LogInformation("Cancelling Vote");
        _voteFailedCallback?.Invoke();
    }

    private void VoteFailed()
    {
        KillTimers();

        if (_voteFailedCallback == null)
        {
            return;
        }

        _logger.LogInformation("Vote Failed");

        _gameServer.Message(HudDestination.Center, $" {ChatColors.Red}Vote Failed");
        _voteFailedCallback();
    }

    private void VoteSuccess()
    {
        KillTimers();

        if (_voteSuccessCallback == null)
        {
            return;
        }

        _logger.LogInformation("Vote Success");
        _gameServer.Message(HudDestination.Center, $" {ChatColors.Red}Vote Succesful");
        _voteSuccessCallback();
    }

    public void KillTimers()
    {
        _voteTimeoutTimer?.Kill();
        _playerMessageTimer?.Kill();
    }

    public void CastVote(CCSPlayerController player, bool vote)
    {
        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();

        if (matchData == null)
        {
            _logger.LogWarning("No match data found");
            return;
        }

        if (IsCaptainVoteOnly())
        {
            if (MatchUtility.GetMemberFromLineup(matchData, player)?.captain == false)
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

        CheckVotes();

        SendVoteMessage();
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

            if (_votes.ContainsKey(player.SteamID))
            {
                continue;
            }

            _gameServer.Message(
                HudDestination.Alert,
                $" {ChatColors.Yellow}{_voteMessage} [{_votes.Count}/{GetExpectedVoteCount()}] ({CommandUtility.PublicChatTrigger}y or {CommandUtility.PublicChatTrigger}n)"
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
            if (MatchUtility.GetMemberFromLineup(matchData, player)?.captain ?? false)
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
            if (totalYesVotes >= 1)
            {
                VoteSuccess();
                return;
            }

            VoteFailed();
            return;
        }

        if (totalYesVotes >= (expectedVoteCount / 2) + 1)
        {
            VoteSuccess();
            return;
        }

        if (fail || _votes.Count >= expectedVoteCount)
        {
            VoteFailed();
        }
    }

    private int GetExpectedVoteCount()
    {
        if (IsCaptainVoteOnly())
        {
            return Math.Min(MatchUtility.Players().Count, 2);
        }

        return MatchUtility.Players().Count;
    }
}
