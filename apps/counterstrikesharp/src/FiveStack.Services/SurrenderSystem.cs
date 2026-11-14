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

    // Only a different match clears it.
    private readonly HashSet<ulong> _reportedAbandons = new HashSet<ulong>();

    // Players the organizers have been told are absent. Coming back takes a
    // player off it, so their next absence is told too.
    private readonly HashSet<ulong> _reportedAbsences = new HashSet<ulong>();
    private Guid? _matchId;

    // Leavers who ran out of time in a match that only reports them once their
    // lineup surrenders. Coming back takes a leaver off it, and so does their
    // lineup being whole without them.
    private readonly HashSet<ulong> _timedOutLeavers = new HashSet<ulong>();

    // Lineup players who left where leaving is not timed. Coming back takes a
    // player off it, and so does their lineup being whole without them.
    private readonly HashSet<ulong> _untimedLeavers = new HashSet<ulong>();

    // A map started play and its untimed leavers are still to be timed.
    private bool _timesAbsentPlayers;

    // Lineups whose leaver ran out of time while another surrender vote was
    // open, with the side the leaver left from.
    private readonly Dictionary<Guid, CsTeam> _waitingLineups = new Dictionary<Guid, CsTeam>();

    // Long enough for the lineup that just voted to read that its vote failed.
    private const float WaitingVoteDelaySeconds = 3;

    // The lineup a leaver's vote was opened for. A vote a player called has none.
    private Guid? _leaverVoteLineup;

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
    }

    public void SetupDisconnectTimer(CsTeam team, ulong steamId, string playerName = "")
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null)
        {
            return;
        }

        if (!TimesLeaver(match))
        {
            _untimedLeavers.Add(steamId);
            return;
        }

        MatchData? matchData = match.GetMatchData();
        if (matchData == null)
        {
            return;
        }

        MatchMember? member = MatchUtility.GetMemberFromLineup(matchData, steamId.ToString(), playerName);
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
        Guid matchId = matchData.id;

        Timer? timer = null;

        // Outlives the map's phase on purpose: a player who quits just before the
        // map ends still runs out of time, only no vote is offered outside play.
        timer = TimerUtility.AddTimer(
            60 * 3,
            () =>
            {
                ForgetFiredTimer(team, steamId, timer!);

                MatchManager? current = _matchService.GetCurrentMatch();
                if (
                    current == null
                    || current.GetMatchData()?.id != matchId
                    || MatchUtility.Players().Any(player => player.SteamID == steamId)
                )
                {
                    return;
                }

                if (current.IsInPlay())
                {
                    OfferLeaverSurrender(lineupId, team);
                }

                PlayerAbandonedMatch(steamId);
                ForgetReplacedLeavers(
                    current.GetMatchData() ?? matchData,
                    MatchUtility
                        .Players()
                        .Select(player => (player.SteamID.ToString(), player.PlayerName)),
                    current.GetExpectedPlayerCount()
                );
            }
        );

        _disconnectTimers[team][steamId] = timer;
    }

    public void MapStatusChanged(eMapStatus previous, eMapStatus status, int mapOrder)
    {
        if (LeaverRuleUtility.TimesAbsentPlayers(LeaverRule(), previous, status, mapOrder))
        {
            _timesAbsentPlayers = true;
        }

        TimeAbsentPlayers();
    }

    // Going live from warmup is only in play once CS2 has ended its warmup, a
    // round later, so where nobody is timed outside play the first round in
    // play times them.
    public void TimeAbsentPlayers()
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (!_timesAbsentPlayers || match == null || matchData == null || !TimesLeaver(match))
        {
            return;
        }

        _timesAbsentPlayers = false;

        HashSet<ulong> timed = _disconnectTimers
            .Values.SelectMany(timers => timers.Keys)
            .Concat(_reportedAbandons)
            .Concat(_reportedAbsences)
            .Concat(_timedOutLeavers)
            .ToHashSet();

        foreach (
            ulong steamId in ReconnectPauseUtility.AbsentToTime(
                matchData,
                MatchUtility
                    .Players()
                    .Select(player => (player.SteamID.ToString(), player.PlayerName)),
                match.GetExpectedPlayerCount(),
                _untimedLeavers,
                timed
            )
        )
        {
            SetupDisconnectTimer(
                LeaverSurrenderSide(
                    matchData,
                    match.GetCurrentMap(),
                    LeaverLineup(matchData, steamId.ToString(), ""),
                    CsTeam.None,
                    _gameServer.GetTotalRoundsPlayed()
                ),
                steamId
            );
        }
    }

    // CounterStrikeSharp hands a fired timer's address to the next timer it
    // makes, so a fired one must never be left to be killed later.
    private void ForgetFiredTimer(CsTeam team, ulong steamId, Timer timer)
    {
        if (
            _disconnectTimers.TryGetValue(team, out Dictionary<ulong, Timer>? timers)
            && timers.GetValueOrDefault(steamId) == timer
        )
        {
            timers.Remove(steamId);
        }
    }

    private bool IsMapInPlay()
    {
        return _matchService.GetCurrentMatch()?.IsInPlay() == true;
    }

    private eLeaverRule LeaverRule()
    {
        return LeaverRuleUtility.For(
            _matchService.GetCurrentMatch()?.GetMatchData(),
            _environmentService.IsOfflineMode()
        );
    }

    private bool TimesLeaver(MatchManager match)
    {
        return LeaverRuleUtility.TimesLeaver(
            LeaverRule(),
            match.IsInPlay(),
            match.gameEnded
                || MatchPhaseUtility.PhaseOf(match.GetCurrentMapStatus()) == eMatchPhase.Over
        );
    }

    // An open vote would turn this one away, so the leaver's lineup waits for it.
    private void OfferLeaverSurrender(Guid? lineupId, CsTeam sideAtDisconnect)
    {
        if (lineupId != null && IsSurrendering())
        {
            _waitingLineups.TryAdd(lineupId.Value, sideAtDisconnect);
            return;
        }

        MatchManager? current = _matchService.GetCurrentMatch();
        MatchData? matchData = current?.GetMatchData();

        if (
            current != null
            && matchData != null
            && lineupId != null
            && ReconnectPauseUtility.IsLineupWhole(
                matchData,
                MatchUtility.Players().Select(player => (player.SteamID.ToString(), player.PlayerName)),
                current.GetExpectedPlayerCount(),
                lineupId.Value
            )
        )
        {
            return;
        }

        SetupSurrender(
            LeaverSurrenderSide(
                matchData,
                current?.GetCurrentMap(),
                lineupId,
                sideAtDisconnect,
                _gameServer.GetTotalRoundsPlayed()
            ),
            forLeaver: true
        );
    }

    // The lineup that just voted has been asked, whichever of its leavers was waiting.
    private void OfferWaitingLineup(Guid votedLineup)
    {
        _waitingLineups.Remove(votedLineup);

        if (_waitingLineups.Count == 0)
        {
            return;
        }

        _matchService.GetCurrentMatch()?.PhaseTimer(WaitingVoteDelaySeconds, OfferNextWaitingLineup);
    }

    private void OfferNextWaitingLineup()
    {
        if (_waitingLineups.Count == 0 || IsSurrendering())
        {
            return;
        }

        KeyValuePair<Guid, CsTeam> next = _waitingLineups.First();
        _waitingLineups.Remove(next.Key);

        OfferLeaverSurrender(next.Key, next.Value);
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
        _timedOutLeavers.Remove(steamId);
        _reportedAbsences.Remove(steamId);
        _untimedLeavers.Remove(steamId);

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

            if (match == null || matchData == null)
            {
                return;
            }

            List<(string SteamId, string Name)> connected = MatchUtility
                .Players()
                .Select(player => (player.SteamID.ToString(), player.PlayerName))
                .ToList();

            ForgetReplacedLeavers(matchData, connected, match.GetExpectedPlayerCount());

            if (LeaverVoteLineupIsWhole(match, connected))
            {
                surrenderingVote?.CancelVote();
            }

            match.timeoutSystem.RosterChanged(connected);

            if (
                !match.timeoutSystem.RosterOwnsPause()
                || ReconnectPauseUtility.ShouldPause(matchData, connected, match.GetExpectedPlayerCount())
            )
            {
                return;
            }

            match.ResumeMatch();
        });
    }

    // A whole lineup has someone in every seat, so a map it gives up later is
    // not given up over the leavers it has replaced. Where a leaver only counts
    // once their lineup surrenders, one still running out of time is replaced
    // too: whoever leaves next starts their own three minutes.
    public void ForgetReplacedLeavers(
        MatchData matchData,
        IEnumerable<(string SteamId, string Name)> connected,
        int expectedPlayers
    )
    {
        List<Guid> shortLineups = ReconnectPauseUtility.ShortLineups(
            matchData,
            connected,
            expectedPlayers
        );

        _timedOutLeavers.RemoveWhere(steamId => IsReplaced(matchData, shortLineups, steamId));
        _untimedLeavers.RemoveWhere(steamId => IsReplaced(matchData, shortLineups, steamId));

        if (LeaverRule() != eLeaverRule.AbandonOnSurrender)
        {
            return;
        }

        List<ulong> replaced = _disconnectTimers
            .Values.SelectMany(timers => timers.Keys)
            .Where(steamId => IsReplaced(matchData, shortLineups, steamId))
            .ToList();

        foreach (ulong steamId in replaced)
        {
            KillDisconnectTimer(steamId);
        }
    }

    private static bool IsReplaced(MatchData matchData, List<Guid> shortLineups, ulong steamId)
    {
        return LeaverLineup(matchData, steamId.ToString(), "") is Guid lineup
            && !shortLineups.Contains(lineup);
    }

    private bool LeaverVoteLineupIsWhole(
        MatchManager match,
        IEnumerable<(string SteamId, string Name)> connected
    )
    {
        MatchData? matchData = match.GetMatchData();

        return _leaverVoteLineup is Guid lineup
            && matchData != null
            && ReconnectPauseUtility.IsLineupWhole(
                matchData,
                connected,
                match.GetExpectedPlayerCount(),
                lineup
            );
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
        Guid? callerLineup = null,
        bool forLeaver = false
    )
    {
        if (LeaverRule() == eLeaverRule.NoSurrender)
        {
            player?.PrintToConsole(_gameServer.Localize(player, "surrender.lan"));
            return;
        }

        if (!IsMapInPlay())
        {
            player?.PrintToConsole(_gameServer.Localize(player, "surrender.not_in_play"));
            return;
        }

        _logger.LogInformation($"Setting up surrender vote for {team}");
        if (surrenderingVote != null && surrenderingVote.IsVoteActive())
        {
            player?.PrintToConsole(_gameServer.Localize(player, "surrender.in_progress"));
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
            player?.PrintToConsole(_gameServer.Localize(player, "surrender.unavailable"));
            return;
        }

        MatchData? surrenderMatch = _matchService.GetCurrentMatch()?.GetMatchData();
        Guid? surrenderingLineup =
            surrenderMatch == null ? null : SurrenderingLineup(surrenderMatch, winningLineup.Value);

        if (surrenderingLineup == null)
        {
            _logger.LogWarning($"No surrendering lineup found for {team}");
            player?.PrintToConsole(_gameServer.Localize(player, "surrender.unavailable"));
            return;
        }

        _logger.LogInformation($"Starting Surrender Vote for {team}");
        _leaverVoteLineup = forLeaver ? surrenderingLineup : null;
        surrenderingVote.StartVote(
            "surrender.vote",
            [],
            new CsTeam[] { CsTeam.Terrorist, CsTeam.CounterTerrorist },
            () =>
            {
                _logger.LogInformation("surrender vote passed");
                Surrender(winningLineup.Value);
                EndVote();
            },
            () =>
            {
                _logger.LogInformation("surrender vote failed");
                surrenderingVote = null;
                OfferWaitingLineup(surrenderingLineup.Value);
            },
            false,
            30,
            voterFilter: voter => CanVoteInSurrender(VoterLineup(voter), surrenderingLineup.Value)
        );
    }

    // An abandon is reported once per match, never once per map, and a leaver's
    // timer runs on into the next map, so a new map clears none of it and a
    // different match clears it all.
    public void OnMatchSetup(MatchData matchData)
    {
        if (_matchId == matchData.id)
        {
            return;
        }

        _matchId = matchData.id;
        _reportedAbandons.Clear();
        _reportedAbsences.Clear();
        _timedOutLeavers.Clear();
        _untimedLeavers.Clear();
        _timesAbsentPlayers = false;
        KillDisconnectTimers();
    }

    // A phase change ends the vote. Leavers' timers keep running to report them.
    public void EndVote()
    {
        surrenderingVote?.Abandon();
        surrenderingVote = null;
        _waitingLineups.Clear();
    }

    private void KillDisconnectTimers()
    {
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

    // `lineupId` is the lineup that WINS the forfeit, not the one giving up.
    public void Surrender(Guid lineupId)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null || !match.IsInPlay())
        {
            _logger.LogInformation("Not surrendering, the map is not in play");
            return;
        }

        _logger.LogInformation($"Surrendering to {lineupId}");

        winningLineupId = lineupId;

        ReportTimedOutLeavers(match.GetMatchData(), lineupId);

        // The winner has to travel with the status. Without it the API records
        // the map as Surrendered with no winner at all.
        match.UpdateMapStatus(eMapStatus.Surrendered, lineupId);

        if (_environmentService.IsOfflineMode())
        {
            EndGameForSurrender(match, lineupId);
        }
    }

    // Sent ahead of the Surrendered status: that ends the match, and the api
    // takes no abandon for a match that is over. A leaver who is back, whose
    // lineup a substitute made whole again, or whose lineup is not the one giving
    // up, is not reported.
    private void ReportTimedOutLeavers(MatchData? matchData, Guid winningLineup)
    {
        Guid? surrenderingLineup =
            matchData == null ? null : SurrenderingLineup(matchData, winningLineup);

        if (matchData == null || surrenderingLineup == null)
        {
            return;
        }

        MatchManager? current = _matchService.GetCurrentMatch();

        if (
            current != null
            && ReconnectPauseUtility.IsLineupWhole(
                matchData,
                MatchUtility.Players().Select(player => (player.SteamID.ToString(), player.PlayerName)),
                current.GetExpectedPlayerCount(),
                surrenderingLineup.Value
            )
        )
        {
            return;
        }

        foreach (ulong steamId in _timedOutLeavers)
        {
            if (
                LeaverLineup(matchData, steamId.ToString(), "") == surrenderingLineup
                && !MatchUtility.Players().Any(player => player.SteamID == steamId)
            )
            {
                ReportAbandon(steamId);
            }
        }
    }

    private const float SurrenderRoundRestartDelay = 5f;

    // Online the api ends the match and stops the server. Offline the round is
    // ended the way CS2's own surrender vote ends it, which ends the game with
    // the surrendering side beaten.
    private void EndGameForSurrender(MatchManager match, Guid winningLineupId)
    {
        // A roster pause never ends itself offline, so the vote mostly passes in
        // one. A paused round may never restart, and a surrendered map has no
        // pause left for anyone to resume.
        _gameServer.SendCommands(["mp_unpause_match"]);

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
        switch (LeaverRule())
        {
            case eLeaverRule.NoSurrender:
                ReportAbsent(steamId);
                return;
            case eLeaverRule.AbandonOnSurrender:
                _timedOutLeavers.Add(steamId);
                return;
            default:
                ReportAbandon(steamId);
                return;
        }
    }

    private void ReportAbandon(ulong steamId)
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

    // Organizers are told once per absence: a timer only runs out once per
    // drop, and only coming back lets the next one be told.
    private void ReportAbsent(ulong steamId)
    {
        if (!_reportedAbsences.Add(steamId))
        {
            return;
        }

        _matchEvents.PublishGameEvent(
            "player-absent",
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
        if (currentMap == null)
        {
            return null;
        }

        if (callerLineup != null)
        {
            if (
                TeamUtility.GetLineupSide(matchData, currentMap, callerLineup.Value, roundsPlayed)
                == CsTeam.None
            )
            {
                return null;
            }

            return SurrenderingLineup(matchData, callerLineup.Value);
        }

        CsTeam winningSide = surrenderingSide == CsTeam.CounterTerrorist ? CsTeam.Terrorist : CsTeam.CounterTerrorist;

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
