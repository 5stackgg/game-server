using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

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

    private Dictionary<CsTeam, Dictionary<ulong, Timer>> _disconnectTimers =
        new Dictionary<CsTeam, Dictionary<ulong, Timer>>();

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

    public void SetupDisconnectTimer(CsTeam team, ulong steamId, string playerName = "")
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
            _disconnectTimers[team] = new Dictionary<ulong, Timer>();
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
    public static CsTeam LeaverSurrenderSide(
        MatchData? matchData,
        MatchMap? currentMap,
        Guid? lineupId,
        CsTeam sideAtDisconnect,
        int roundsPlayed
    )
    {
        if (matchData == null || currentMap == null || lineupId == null)
        {
            return sideAtDisconnect;
        }

        CsTeam side = TeamUtility.GetLineupSide(matchData, currentMap, lineupId.Value, roundsPlayed);

        if (side != CsTeam.Terrorist && side != CsTeam.CounterTerrorist)
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
    // player list, so the roster is only whole a frame later.
    public void ResumeIfRosterWhole()
    {
        Server.NextFrame(() =>
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

        foreach (Dictionary<ulong, Timer> timers in _disconnectTimers.Values)
        {
            if (timers.Remove(steamId, out Timer? timer))
            {
                timer?.Kill();
                killed = true;
            }
        }

        return killed;
    }

    public void SetupSurrender(
        CsTeam team,
        CCSPlayerController? player = null,
        Guid? callerLineup = null
    )
    {
        if (
            _matchService.GetCurrentMatch()?.GetMatchData()?.is_lan == true
            && !_environmentService.IsOfflineMode()
        )
        {
            player?.PrintToConsole(" There is no surrender in a LAN match");
            return;
        }

        _logger.LogInformation($"Setting up surrender vote for {team}");
        if (surrenderingVote != null && surrenderingVote.IsVoteActive())
        {
            player?.PrintToConsole(" A surrender vote is already in progress");
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
        CsTeam winningTeam =
            team == CsTeam.CounterTerrorist ? CsTeam.Terrorist : CsTeam.CounterTerrorist;

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
            player?.PrintToConsole(" Unable to start a surrender vote right now");
            return;
        }

        MatchData? surrenderMatch = _matchService.GetCurrentMatch()?.GetMatchData();
        Guid? surrenderingLineup =
            surrenderMatch == null ? null : SurrenderingLineup(surrenderMatch, winningLineup.Value);

        if (surrenderingLineup == null)
        {
            _logger.LogWarning($"No surrendering lineup found for {team}");
            player?.PrintToConsole(" Unable to start a surrender vote right now");
            return;
        }

        _logger.LogInformation($"Starting Surrender Vote for {team}");
        surrenderingVote.StartVote(
            "Surrender",
            new CsTeam[] { CsTeam.Terrorist, CsTeam.CounterTerrorist },
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
                timer?.Kill();
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

        CsTeam winningSide = TeamUtility.GetLineupSide(
            matchData,
            currentMap,
            winningLineupId,
            _gameServer.GetTotalRoundsPlayed()
        );

        if (winningSide != CsTeam.Terrorist && winningSide != CsTeam.CounterTerrorist)
        {
            _logger.LogWarning($"No side found for surrender winner {winningLineupId}");
            return;
        }

        RoundEndReason reason =
            winningSide == CsTeam.CounterTerrorist
                ? RoundEndReason.TerroristsSurrender
                : RoundEndReason.CTsSurrender;

        _logger.LogInformation($"Ending the game for the surrender: {reason}");

        // A stale gamedata signature throws here. The next round start ends the map.
        try
        {
            rules.TerminateRound(SurrenderRoundRestartDelay, reason);
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
    public static bool CanCallSurrender(CsTeam team, bool isLineupMember)
    {
        return isLineupMember && (team == CsTeam.Terrorist || team == CsTeam.CounterTerrorist);
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

    private Guid? VoterLineup(CCSPlayerController player)
    {
        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();

        return matchData == null
            ? null
            : LineupMembership.LineupOf(matchData, player.SteamID.ToString(), player.PlayerName);
    }

    // A player calling surrender gives up for their own lineup, whichever side
    // the round count says it is on: rounds played moves at round end, before
    // anyone is swapped. Only a timed-out leaver, with no caller, goes by side.
    public static Guid? WinningLineup(
        MatchData matchData,
        MatchMap? currentMap,
        Guid? callerLineup,
        CsTeam surrenderingSide,
        int roundsPlayed
    )
    {
        if (callerLineup != null)
        {
            return SurrenderingLineup(matchData, callerLineup.Value);
        }

        CsTeam winningSide = surrenderingSide == CsTeam.CounterTerrorist ? CsTeam.Terrorist : CsTeam.CounterTerrorist;

        if (currentMap == null)
        {
            return null;
        }

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
