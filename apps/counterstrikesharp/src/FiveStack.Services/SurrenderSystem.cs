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

    public void SetupDisconnectTimer(CsTeam team, ulong steamId)
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
            _disconnectTimers[team] = new Dictionary<ulong, Timer>();
        }

        Guid? lineupId = member.match_lineup_id == Guid.Empty ? null : member.match_lineup_id;

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

    public static Guid? LeaverLineup(MatchData matchData, string steamId)
    {
        Guid? lineupId = MatchUtility.GetMemberFromLineup(matchData, steamId, "")?.match_lineup_id;
        return lineupId == Guid.Empty ? null : lineupId;
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

    public void SetupSurrender(CsTeam team, CCSPlayerController? player = null)
    {
        _logger.LogInformation($"Setting up surrender vote for {team}");
        if (surrenderingVote != null && surrenderingVote.IsVoteActive())
        {
            player?.PrintToConsole(" A surrender vote is already in progress");
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

        Guid? winningLineup = ResolveLineupForSide(winningTeam);

        if (winningLineup == null)
        {
            _logger.LogWarning($"No lineup id found for {winningTeam}");
            player?.PrintToConsole(" Unable to start a surrender vote right now");
            return;
        }

        _logger.LogInformation($"Starting Surrender Vote for {team}");
        surrenderingVote.StartVote(
            "Surrender",
            new CsTeam[] { team },
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
    private Guid? ResolveLineupForSide(CsTeam team)
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
