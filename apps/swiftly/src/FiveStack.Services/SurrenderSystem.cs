using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Natives;
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
    private readonly EnvironmentService _environmentService;
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
        IServiceProvider serviceProvider,
        EnvironmentService environmentService
    )
    {
        _logger = logger;
        _matchEvents = matchEvents;
        _matchService = matchService;
        _gameServer = gameServer;
        _serviceProvider = serviceProvider;
        _environmentService = environmentService;
        Reset();
    }

    public void SetupDisconnectTimer(Team team, ulong steamId, string playerName = "")
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

        // A LAN drop is a dead machine or network far more often than a leaver.
        if (matchData.is_lan && !_environmentService.IsOfflineMode())
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

        Guid? lineupId = LeaverLineup(matchData, steamId.ToString(), playerName);

        _disconnectTimers[team][steamId] = TimerUtility.AddTimer(
            60 * 3,
            () =>
            {
                MatchManager? current = _matchService.GetCurrentMatch();
                SetupSurrender(
                    LeaverSurrenderSide(
                        current?.GetMatchData(),
                        current?.GetCurrentMap(),
                        lineupId,
                        team,
                        _gameServer.GetTotalRoundsPlayed()
                    )
                );
                PlayerAbandonedMatch(steamId);
            }
        );
    }

    public static Guid? LeaverLineup(MatchData matchData, string steamId, string playerName)
    {
        return LineupMembership.LineupOf(matchData, steamId, playerName);
    }

    // The side the leaver's lineup plays when the timer runs out. A halftime in
    // those three minutes swaps it away from the side they left from.
    public static Team LeaverSurrenderSide(
        MatchData? matchData,
        MatchMap? currentMap,
        Guid? lineupId,
        Team sideAtDisconnect,
        int roundsPlayed
    )
    {
        if (matchData == null || currentMap == null || lineupId == null)
        {
            return sideAtDisconnect;
        }

        Team side = TeamUtility.GetLineupSide(matchData, currentMap, lineupId.Value, roundsPlayed);

        if (side != Team.T && side != Team.CT)
        {
            return sideAtDisconnect;
        }

        return side;
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

            if (
                ReconnectPauseUtility.ShouldPause(
                    matchData,
                    MatchUtility.Players().Select(player => (player.SteamID.ToString(), player.Name)),
                    match.GetExpectedPlayerCount()
                )
            )
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

    public void SetupSurrender(Team team, IPlayer? player = null, Guid? callerLineup = null)
    {
        if (
            _matchService.GetCurrentMatch()?.GetMatchData()?.is_lan == true
            && !_environmentService.IsOfflineMode()
        )
        {
            if (player != null)
            {
                player.SendConsole(" There is no surrender in a LAN match");
            }
            return;
        }

        _logger.LogInformation($"Setting up surrender vote for {team}");
        if (surrenderingVote != null && surrenderingVote.IsVoteActive())
        {
            if (player != null)
            {
                player.SendConsole(" A surrender vote is already in progress");
            }
            return;
        }

        if (!CanCallSurrender(team, isLineupMember: true))
        {
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

        MatchManager? current = _matchService.GetCurrentMatch();
        MatchData? currentMatch = current?.GetMatchData();
        Guid? winningLineup =
            currentMatch == null
                ? null
                : WinningLineup(
                    currentMatch,
                    current?.GetCurrentMap(),
                    callerLineup,
                    team,
                    _gameServer.GetTotalRoundsPlayed()
                );

        if (winningLineup == null)
        {
            _logger.LogWarning($"No lineup id found for {winningTeam}");
            if (player != null)
            {
                player.SendConsole(" Unable to start a surrender vote right now");
            }
            return;
        }

        MatchData? surrenderMatch = _matchService.GetCurrentMatch()?.GetMatchData();
        Guid? surrenderingLineup =
            surrenderMatch == null ? null : SurrenderingLineup(surrenderMatch, winningLineup.Value);

        if (surrenderingLineup == null)
        {
            _logger.LogWarning($"No surrendering lineup found for {team}");
            if (player != null)
            {
                player.SendConsole(" Unable to start a surrender vote right now");
            }
            return;
        }

        _logger.LogInformation($"Starting Surrender Vote for {team}");
        surrenderingVote.StartVote(
            "surrender.vote",
            [],
            new Team[] { Team.T, Team.CT },
            () =>
            {
                _logger.LogInformation("surrender vote passed");
                Surrender(winningLineup.Value);
                Reset();
            },
            () =>
            {
                _logger.LogInformation("surrender vote failed");
                surrenderingVote = null;
            },
            false,
            30,
            voterFilter: voter => CanVoteInSurrender(VoterLineup(voter), surrenderingLineup.Value)
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
        surrenderingVote?.Abandon();
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

        if (_environmentService.IsOfflineMode())
        {
            EndGameForSurrender(match, lineupId);
        }
    }

    private const float SurrenderRoundRestartDelay = 5f;

    // Online the api ends the match and stops the server. Offline the round is
    // ended the way CS2's own surrender vote ends it, which ends the game with
    // the surrendering side beaten.
    private void EndGameForSurrender(MatchManager match, Guid winningLineupId)
    {
        MatchData? matchData = match.GetMatchData();
        MatchMap? currentMap = match.GetCurrentMap();
        CCSGameRules? rules = MatchUtility.Rules();

        if (matchData == null || currentMap == null || rules == null)
        {
            return;
        }

        Team winningSide = TeamUtility.GetLineupSide(
            matchData,
            currentMap,
            winningLineupId,
            _gameServer.GetTotalRoundsPlayed()
        );

        if (winningSide != Team.T && winningSide != Team.CT)
        {
            _logger.LogWarning($"No side found for surrender winner {winningLineupId}");
            return;
        }

        RoundEndReason reason =
            winningSide == Team.CT ? RoundEndReason.TerroristsSurrender : RoundEndReason.CTsSurrender;

        _logger.LogInformation($"Ending the game for the surrender: {reason}");

        // A stale gamedata signature throws here. The next round start ends the map.
        try
        {
            rules.TerminateRound(reason, SurrenderRoundRestartDelay);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to end the game for the surrender");
        }
    }

    public Guid? GetWinningLineupId()
    {
        return winningLineupId;
    }

    public void PlayerAbandonedMatch(ulong steamId)
    {
        if (
            _matchService.GetCurrentMatch()?.GetMatchData()?.is_lan == true
            && !_environmentService.IsOfflineMode()
        )
        {
            return;
        }

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

    // Only someone playing on a side can give the map up. Anyone else opened a
    // vote for whatever team they sat on and handed the map to the other side.
    public static bool CanCallSurrender(Team team, bool isLineupMember)
    {
        return isLineupMember && (team == Team.T || team == Team.CT);
    }

    // The lineup giving up is the one that did not win.
    public static Guid? SurrenderingLineup(MatchData matchData, Guid winningLineupId)
    {
        if (winningLineupId == matchData.lineup_1_id)
        {
            return matchData.lineup_2_id;
        }

        if (winningLineupId == matchData.lineup_2_id)
        {
            return matchData.lineup_1_id;
        }

        return null;
    }

    // The vote belongs to the lineup, not to a side: sides move at round end,
    // before anyone is swapped, and halftime can fall inside the vote.
    public static bool CanVoteInSurrender(Guid? voterLineupId, Guid surrenderingLineupId)
    {
        return voterLineupId == surrenderingLineupId;
    }

    private Guid? VoterLineup(IPlayer player)
    {
        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();

        return matchData == null
            ? null
            : LineupMembership.LineupOf(matchData, player.SteamID.ToString(), player.Name);
    }

    // A player calling surrender gives up for their own lineup, whichever side
    // the round count says it is on: rounds played moves at round end, before
    // anyone is swapped. Only a timed-out leaver, with no caller, goes by side.
    public static Guid? WinningLineup(
        MatchData matchData,
        MatchMap? currentMap,
        Guid? callerLineup,
        Team surrenderingSide,
        int roundsPlayed
    )
    {
        if (currentMap == null)
        {
            return null;
        }

        if (callerLineup != null)
        {
            if (
                TeamUtility.GetLineupSide(matchData, currentMap, callerLineup.Value, roundsPlayed)
                == Team.None
            )
            {
                return null;
            }

            return SurrenderingLineup(matchData, callerLineup.Value);
        }

        Team winningSide = surrenderingSide == Team.CT ? Team.T : Team.CT;

        if (
            TeamUtility.GetLineupSide(matchData, currentMap, matchData.lineup_1_id, roundsPlayed)
            == winningSide
        )
        {
            return matchData.lineup_1_id;
        }

        if (
            TeamUtility.GetLineupSide(matchData, currentMap, matchData.lineup_2_id, roundsPlayed)
            == winningSide
        )
        {
            return matchData.lineup_2_id;
        }

        return null;
    }
}
