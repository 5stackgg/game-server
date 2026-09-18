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

    // Survives Reset(), which runs on every map of a series.
    private readonly HashSet<ulong> _reportedAbandons = new HashSet<ulong>();
    private Guid? _reportedAbandonsMatchId;

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

        // Overwriting the slot without killing what is in it leaves a live timer
        // nothing can reach, and it fires its own abandon three minutes later.
        KillDisconnectTimer(steamId);

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

    // we dont pass the team in because they may not be on the team immediately after reconnecting
    public void CancelDisconnectTimer(ulong steamId)
    {
        if (!KillDisconnectTimer(steamId))
        {
            return;
        }

        ResumeIfRosterWhole();
    }

    // player_connect_full runs before the connecting client is on the runtime's
    // player list, so the roster is only whole a tick later.
    public void ResumeIfRosterWhole()
    {
        MatchUtility.Core.Scheduler.NextTick(() =>
        {
            MatchManager? match = _matchService.GetCurrentMatch();
            MatchData? matchData = match?.GetMatchData();

            if (match == null || matchData == null || !match.IsPaused())
            {
                return;
            }

            // A tactical or technical pause is released by the teams themselves.
            if (match.timeoutSystem.ShouldRequireTeamResume())
            {
                return;
            }

            if (MatchUtility.ConnectedRosterCount(matchData) < match.GetExpectedPlayerCount())
            {
                return;
            }

            Reset();
            match.ResumeMatch();
        });
    }

    // Swept across every bucket, not just the team the player is on now: a
    // player who reconnects onto the other side leaves a timer filed under the
    // side they left from.
    private bool KillDisconnectTimer(ulong steamId)
    {
        bool killed = false;

        foreach (Dictionary<ulong, CancellationTokenSource> timers in _disconnectTimers.Values)
        {
            if (timers.Remove(steamId, out CancellationTokenSource? timer))
            {
                TimerUtility.Kill(timer);
                killed = true;
            }
        }

        return killed;
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

        // The winner is the other side, and it is resolved to a lineup NOW,
        // under the sides the vote was called with. Resolving when the vote
        // passes reads the sides 30 seconds later, and a halftime inside that
        // window swaps them -- handing the map to the team that just gave up.
        Team winningTeam = team == Team.CT ? Team.T : Team.CT;

        Guid? winningLineup = ResolveLineupForSide(winningTeam);

        if (winningLineup == null)
        {
            _logger.LogWarning($"No lineup id found for {winningTeam}");
            if (player != null)
            {
                player.SendConsole(" Unable to start a surrender vote right now");
            }
            return;
        }

        _logger.LogInformation($"Starting Surrender Vote for {team}");
        surrenderingVote.StartVote(
            "Surrender",
            new Team[] { team },
            () =>
            {
                _logger.LogInformation("surrender vote passed");
                Surrender(winningLineup.Value);
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

    // Abandons are reported once per match, never once per map, so the reported
    // set is only cleared when a different match loads.
    public void OnMatchSetup(MatchData matchData)
    {
        if (_reportedAbandonsMatchId == matchData.id)
        {
            return;
        }

        _reportedAbandonsMatchId = matchData.id;
        _reportedAbandons.Clear();
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

    // Which lineup is currently playing as `team`, side swaps included. The
    // previous comparison was against lineup.name, which is the team's display
    // name ("Theft's Team") and never literally "CT"/"TERRORIST" -- so it was
    // always false and every surrender fell through to lineup_2, handing the
    // win to whichever side happened to be lineup 2 regardless of who forfeited.
    private Guid? ResolveLineupForSide(Team team)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null)
        {
            return null;
        }

        MatchData? matchData = match.GetMatchData();
        MatchMap? currentMap = match.GetCurrentMap();
        if (matchData == null || currentMap == null)
        {
            return null;
        }

        int roundsPlayed = _gameServer.GetTotalRoundsPlayed();

        if (
            TeamUtility.GetLineupSide(matchData, currentMap, matchData.lineup_1_id, roundsPlayed)
            == team
        )
        {
            return matchData.lineup_1_id;
        }

        if (
            TeamUtility.GetLineupSide(matchData, currentMap, matchData.lineup_2_id, roundsPlayed)
            == team
        )
        {
            return matchData.lineup_2_id;
        }

        return null;
    }

    // `lineupId` is the lineup that WINS the forfeit, not the one giving up.
    public void Surrender(Guid lineupId)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null)
        {
            return;
        }

        _logger.LogInformation($"Surrendering to {lineupId}");

        winningLineupId = lineupId;

        // The winner has to travel with the status. Without it the API records
        // the map as Surrendered with no winner at all.
        match.UpdateMapStatus(eMapStatus.Surrendered, lineupId);
    }

    public Guid? GetWinningLineupId()
    {
        return winningLineupId;
    }

    public void PlayerAbandonedMatch(ulong steamId)
    {
        // The api escalates the ban on every report, so one leave reported twice
        // -- after a reconnect, or again on the next map -- costs the player a
        // longer ban than they earned.
        if (!_reportedAbandons.Add(steamId))
        {
            _logger.LogInformation($"{steamId} was already reported as abandoned this match");
            return;
        }

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
