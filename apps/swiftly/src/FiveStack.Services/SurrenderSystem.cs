using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

public class SurrenderSystem
{
    private readonly MatchEvents _matchEvents;
    private readonly MatchService _matchService;
    private readonly GameServer _gameServer;
    private readonly ILogger<ReadySystem> _logger;
    private readonly IServiceProvider _serviceProvider;
    public VoteSystem? surrenderingVote;

    private Dictionary<Team, Dictionary<ulong, CancellationTokenSource>> _disconnectTimers =
        new Dictionary<Team, Dictionary<ulong, CancellationTokenSource>>();

    private Guid? winningLineupId;

    public SurrenderSystem(
        ILogger<ReadySystem> logger,
        MatchEvents matchEvents,
        MatchService matchService,
        GameServer gameServer,
        IServiceProvider serviceProvider
    )
    {
        _logger = logger;
        _matchEvents = matchEvents;
        _matchService = matchService;
        _gameServer = gameServer;
        _serviceProvider = serviceProvider;
        Reset();
    }

    public void SetupDisconnectTimer(Team team, ulong steamId)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null || !match.IsInPlay())
        {
            return;
        }

        MatchData? matchData = match.GetMatchData();
        if (matchData == null)
        {
            return;
        }

        MatchMember? member = MatchUtility.GetMemberFromLineup(matchData, steamId.ToString(), "");
        if (member == null)
        {
            return;
        }

        if (!_disconnectTimers.ContainsKey(team))
        {
            _disconnectTimers[team] = new Dictionary<ulong, CancellationTokenSource>();
        }

        _disconnectTimers[team][steamId] = TimerUtility.AddTimer(
            60 * 3,
            () =>
            {
                SetupSurrender(team);
                PlayerAbandonedMatch(steamId);
            }
        );
    }

    public void CancelDisconnectTimer(ulong steamId)
    {
        bool canceledTimer = false;
        foreach (var _team in MatchUtility.Teams())
        {
            Team team = TeamUtility.TeamNumToTeam(_team.TeamNum);

            if (_disconnectTimers.ContainsKey(team))
            {
                if (_disconnectTimers[team].ContainsKey(steamId))
                {
                    TimerUtility.Kill(_disconnectTimers[team][steamId]);
                    _disconnectTimers[team].Remove(steamId);
                    canceledTimer = true;
                }
            }
        }

        if (!canceledTimer)
        {
            return;
        }

        int currentPlayers = MatchUtility.PlayerCount();

        int expectedPlayers = _matchService.GetCurrentMatch()?.GetExpectedPlayerCount() ?? 10;

        if (
            _matchService.GetCurrentMatch()?.IsPaused() == true
            && currentPlayers == expectedPlayers
        )
        {
            Reset();
            _matchService.GetCurrentMatch()?.ResumeMatch();
        }
    }

    public void SetupSurrender(Team team, IPlayer? player = null)
    {
        _logger.LogInformation($"Setting up surrender vote for {team}");
        if (surrenderingVote != null && surrenderingVote.IsVoteActive())
        {
            if (player != null)
            {
                player.SendConsole(" A surrender vote is already in progress");
            }
            return;
        }

        surrenderingVote = _serviceProvider.GetRequiredService(typeof(VoteSystem)) as VoteSystem;

        if (surrenderingVote == null)
        {
            return;
        }

        // Surrender() takes the team that WINS, not the one giving up.
        Team winningTeam = team == Team.CT ? Team.T : Team.CT;

        _logger.LogInformation($"Starting Surrender Vote for {team}");
        surrenderingVote.StartVote(
            "Surrender",
            new Team[] { team },
            () =>
            {
                _logger.LogInformation("surrender vote passed");
                Surrender(winningTeam);
                Reset();
            },
            () =>
            {
                _logger.LogInformation("surrender vote failed");
                Reset();
            },
            false,
            30
        );
    }

    public void Reset()
    {
        surrenderingVote = null;

        foreach (var team in _disconnectTimers.Keys)
        {
            foreach (var timer in _disconnectTimers[team].Values)
            {
                TimerUtility.Kill(timer);
            }
        }
        _disconnectTimers.Clear();
    }

    public bool IsSurrendering()
    {
        return surrenderingVote != null && surrenderingVote.IsVoteActive();
    }

    public void RemovePlayerVoteOnDisconnect(ulong steamId)
    {
        surrenderingVote?.RemovePlayerVote(steamId);
    }

    // `team` is the side that WINS the forfeit, not the one giving up.
    public void Surrender(Team team)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null)
        {
            return;
        }

        MatchData? matchData = match.GetMatchData();
        MatchMap? currentMap = match.GetCurrentMap();
        if (matchData == null || currentMap == null)
        {
            return;
        }

        // Resolve which lineup is currently playing as `team`, side swaps
        // included. The previous comparison was against lineup.name, which is
        // the team's display name ("Theft's Team") and never literally
        // "CT"/"TERRORIST" -- so it was always false and every surrender fell
        // through to lineup_2, handing the win to whichever side happened to
        // be lineup 2 regardless of who actually forfeited.
        int roundsPlayed = _gameServer.GetTotalRoundsPlayed();
        Guid? lineup_id = null;

        if (
            TeamUtility.GetLineupSide(matchData, currentMap, matchData.lineup_1_id, roundsPlayed)
            == team
        )
        {
            lineup_id = matchData.lineup_1_id;
        }
        else if (
            TeamUtility.GetLineupSide(matchData, currentMap, matchData.lineup_2_id, roundsPlayed)
            == team
        )
        {
            lineup_id = matchData.lineup_2_id;
        }

        if (lineup_id == null)
        {
            _logger.LogWarning($"No lineup id found for {team}");
            return;
        }

        _logger.LogInformation($"Surrendering to {team}:{lineup_id.Value}");

        winningLineupId = lineup_id.Value;

        // The winner has to travel with the status. Without it the API records
        // the map as Surrendered with no winner at all.
        match.UpdateMapStatus(eMapStatus.Surrendered, lineup_id.Value);
    }

    public Guid? GetWinningLineupId()
    {
        return winningLineupId;
    }

    public void PlayerAbandonedMatch(ulong steamId)
    {
        _matchEvents.PublishGameEvent(
            "abandoned",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "steam_id", steamId.ToString() },
            }
        );
    }
}
