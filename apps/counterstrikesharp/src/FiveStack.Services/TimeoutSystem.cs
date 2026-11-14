using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace FiveStack;

public class TimeoutSystem
{
    private readonly HashSet<CsTeam> _teamsPendingResume = new();
    private bool _requiresTeamResumeForCurrentPause;
    private readonly ReconnectTimeouts _reconnectTimeouts = new();
    private Timer? _reconnectTimer;
    private DateTime? _reconnectTimeoutEnds;
    private bool _reconnectTimeoutOwed;
    private bool _waitsOnEmptyLineup;
    private readonly MatchEvents _matchEvents;
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly GameBackUpRounds _backUpManagement;
    private readonly ILogger<TimeoutSystem> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly CoachSystem _coachSystem;
    private readonly CaptainSystem _captainSystem;
    private readonly CameraSystem _cameraSystem;
    public VoteSystem? pauseVote;
    public VoteSystem? resumeVote;

    public TimeoutSystem(
        ILogger<TimeoutSystem> logger,
        MatchEvents matchEvents,
        GameServer gameServer,
        MatchService matchService,
        GameBackUpRounds backUpManagement,
        IServiceProvider serviceProvider,
        CoachSystem coachSystem,
        CaptainSystem captainSystem,
        CameraSystem cameraSystem
    )
    {
        _logger = logger;
        _matchEvents = matchEvents;
        _gameServer = gameServer;
        _matchService = matchService;
        _serviceProvider = serviceProvider;
        _backUpManagement = backUpManagement;
        _coachSystem = coachSystem;
        _captainSystem = captainSystem;
        _cameraSystem = cameraSystem;
    }

    public void RemovePlayerVoteOnDisconnect(ulong steamId)
    {
        pauseVote?.RemovePlayerVote(steamId);
        resumeVote?.RemovePlayerVote(steamId);
    }

    public void Reset()
    {
        pauseVote?.Abandon();
        pauseVote = null;
        resumeVote?.Abandon();
        resumeVote = null;
        ClearPendingTeamResumes();
        StopReconnectTimeout();
    }

    public void RequestPause(CCSPlayerController? player)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null || !match.IsInPlay() || _backUpManagement.IsResettingRound())
        {
            _gameServer.Message(
                HudDestination.Chat,
                _gameServer.Localize(player, "timeout.cannot_pause_not_live", ChatColors.Red),
                player
            );
            return;
        }

        if (IsTimeoutActive())
        {
            SendTimeoutAlreadyActiveMessage(player);
            return;
        }

        if (ShouldRequireTeamResume())
        {
            SendAlreadyPausedMessage(player);
            return;
        }

        Func<CCSPlayerController?, string> pauseMessage = viewer =>
            _gameServer.Localize(viewer, "timeout.admin_paused");

        if (player != null)
        {
            if (!CanPause(player))
            {
                if (pauseVote != null && pauseVote.IsVoteActive())
                {
                    pauseVote.CastVote(player, true);
                    return;
                }

                pauseVote = _serviceProvider.GetRequiredService(typeof(VoteSystem)) as VoteSystem;

                if (pauseVote != null)
                {
                    pauseVote.StartVote(
                        "timeout.vote.technical",
                        [],
                        new CsTeam[] { CsTeam.CounterTerrorist, CsTeam.Terrorist },
                        (
                            () =>
                            {
                                _logger.LogInformation("technical pause vote passed");
                                PauseTechMatch(viewer =>
                                    _gameServer.Localize(viewer, "timeout.vote.technical_passed")
                                );
                                pauseVote = null;
                            }
                        ),
                        () =>
                        {
                            _logger.LogInformation("technical pause vote failed");
                            pauseVote = null;
                        },
                        true,
                        30
                    );

                    if (player != null && pauseVote != null)
                    {
                        pauseVote.CastVote(player, true);
                    }
                }

                return;
            }

            string pausedBy = player.PlayerName;
            pauseMessage = viewer =>
                _gameServer.Localize(viewer, "timeout.player_paused", pausedBy, ChatColors.Red);
        }

        PauseTechMatch(pauseMessage);
    }

    private bool CanPause(CCSPlayerController? player)
    {
        if (player == null)
        {
            return true;
        }

        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();

        return TimeoutUtility.CanPause(
            GetTechnicalPauseSetting(),
            ConnectedRole(player, matchData),
            LineupRole(player, matchData),
            _coachSystem.IsCoach(player, player.Team),
            _captainSystem.IsCaptain(player, player.Team)
        );
    }

    private bool CanCallTacticalTimeout(CCSPlayerController? player)
    {
        if (player == null)
        {
            return true;
        }

        bool isCoach = _coachSystem.IsCoach(player, player.Team);
        bool isCaptain = _captainSystem.IsCaptain(player, player.Team);

        switch (GetTacticalTimeoutSetting())
        {
            case eTimeoutSettings.Coach:
                if (!isCoach)
                {
                    return false;
                }
                break;
            case eTimeoutSettings.CoachAndCaptains:
                if (!isCoach && !isCaptain)
                {
                    return false;
                }
                break;
            case eTimeoutSettings.Admin:
                return false;
        }

        return true;
    }

    private void CannotPauseMessage(CCSPlayerController? player, string refusedKey)
    {
        _gameServer.Message(
            HudDestination.Chat,
            _gameServer.Localize(
                player,
                "timeout.not_allowed",
                ChatColors.Red,
                _gameServer.Localize(player, refusedKey)
            ),
            player
        );
    }

    public void RequestResume(CCSPlayerController? player)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (match == null || matchData == null)
        {
            return;
        }

        bool somethingToResume = TimeoutUtility.HasPauseToResume(
            match.GetCurrentMapStatus(),
            MatchUtility.Rules()?.MatchWaitingForResume == true
        );

        if (player != null && !somethingToResume)
        {
            if (!IsTimeoutActive())
            {
                _gameServer.Message(
                    HudDestination.Chat,
                    _gameServer.Localize(player, "timeout.not_paused", ChatColors.Red),
                    player
                );
            }

            return;
        }

        Func<CCSPlayerController?, string> resumeMessage = viewer =>
            _gameServer.Localize(viewer, "timeout.admin_resumed");

        // Refuse while a required camera is still down, on the same terms as the
        // empty-team gate below: the pause exists because someone is unwatched,
        // so resuming before that is fixed defeats the whole point. Admins keep
        // their override for a camera that is never coming back.
        if (
            player != null
            && !IsAdminOrOrganizer(player, matchData)
            && _cameraSystem.IsBlocking()
        )
        {
            _gameServer.Message(
                HudDestination.Chat,
                _gameServer.Localize(player, "camera.cannot_resume", _cameraSystem.OfflineNames()),
                player
            );
            return;
        }

        // Refuse while a side has nobody in the server. The match pauses itself
        // when it goes short-handed, and resuming into an empty team just plays
        // rounds out against nobody -- free rounds for whoever is still here.
        // Console/RCON is unaffected; this only gates the chat command, and
        // admins keep their override -- they are the ones who have to unstick a
        // match nobody is coming back to.
        if (player != null && !IsAdminOrOrganizer(player, matchData) && IsATeamEmpty())
        {
            _gameServer.Message(
                HudDestination.Chat,
                _gameServer.Localize(player, "timeout.cannot_resume_empty_team"),
                player
            );
            return;
        }

        if (player != null)
        {
            if (ShouldRequireTeamResume())
            {
                if (IsAdminOrOrganizer(player, matchData))
                {
                    Resume(match, resumeMessage);
                    return;
                }

                if (!CanPause(player))
                {
                    CannotPauseMessage(player, "timeout.not_allowed.resume");
                    return;
                }

                if (!_teamsPendingResume.Contains(player.Team))
                {
                    _gameServer.Message(
                        HudDestination.Chat,
                        _gameServer.Localize(player, "timeout.team_already_resumed", ChatColors.Red),
                        player
                    );
                    return;
                }

                _teamsPendingResume.Remove(player.Team);
                if (ShouldRequireTeamResume())
                {
                    string teamResumedBy = player.PlayerName;
                    CsTeam resumedFor = player.Team;

                    _gameServer.MessageEach(
                        HudDestination.Alert,
                        viewer =>
                            _gameServer.Localize(
                                viewer,
                                "timeout.team_resumed",
                                teamResumedBy,
                                ChatColors.Red,
                                _gameServer.Localize(viewer, TeamUtility.TeamNameKey(resumedFor))
                            )
                    );
                    return;
                }
            }

            if (!CanPause(player))
            {
                if (resumeVote != null && resumeVote.IsVoteActive())
                {
                    resumeVote.CastVote(player, true);
                    return;
                }

                resumeVote = _serviceProvider.GetRequiredService(typeof(VoteSystem)) as VoteSystem;

                if (resumeVote != null)
                {
                    resumeVote.StartVote(
                        "timeout.vote.resume",
                        [],
                        new CsTeam[] { CsTeam.CounterTerrorist, CsTeam.Terrorist },
                        (
                            () =>
                            {
                                _logger.LogInformation("resume vote passed");
                                resumeVote = null;

                                if (_cameraSystem.IsBlocking())
                                {
                                    _gameServer.MessageEach(
                                        HudDestination.Chat,
                                        viewer =>
                                            _gameServer.Localize(
                                                viewer,
                                                "camera.cannot_resume",
                                                _cameraSystem.OfflineNames()
                                            )
                                    );
                                    return;
                                }

                                _matchService
                                    .GetCurrentMatch()
                                    ?.ResumeMatch(viewer =>
                                        _gameServer.Localize(viewer, "timeout.vote.resume_passed")
                                    );
                            }
                        ),
                        () =>
                        {
                            _logger.LogInformation("resume vote failed");
                            resumeVote = null;
                        },
                        true,
                        30
                    );

                    if (player != null && resumeVote != null)
                    {
                        resumeVote.CastVote(player, true);
                    }
                }

                return;
            }

            string resumedBy = player.PlayerName;
            resumeMessage = viewer =>
                _gameServer.Localize(viewer, "timeout.player_resumed", resumedBy, ChatColors.Red);
        }

        Resume(match, resumeMessage);
    }

    // Only staff and the console get past the camera gate, so play going on with
    // a camera still down is them overriding it.
    private void Resume(MatchManager match, Func<CCSPlayerController?, string> message)
    {
        ClearPendingTeamResumes();
        match.ResumeMatch(message);

        if (!match.IsPaused() && _cameraSystem.IsBlocking())
        {
            _cameraSystem.Override();
        }
    }

    public void ClearPendingTeamResumes()
    {
        _teamsPendingResume.Clear();
        _requiresTeamResumeForCurrentPause = false;
    }

    public bool ShouldRequireTeamResume()
    {
        return _requiresTeamResumeForCurrentPause && _teamsPendingResume.Count > 0;
    }

    // On a LAN someone at the table says the player is back, so the roster
    // never ends its own pause there.
    public bool RosterOwnsPause()
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        return match != null
            && match.GetPauseReason() == ePauseReason.Roster
            && !match.IsLan()
            && !ShouldRequireTeamResume()
            && !_cameraSystem.IsBlocking();
    }

    // A player at the table asked, so unlike the roster's own resume this ends
    // a LAN pause too. Anyone else in the server is only told who is out.
    public void CheckRoster(CCSPlayerController player)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (match == null || matchData == null)
        {
            return;
        }

        List<(string SteamId, string Name)> connected = MatchUtility
            .Players()
            .Select(present => (present.SteamID.ToString(), present.PlayerName))
            .ToList();

        eRosterCheck check = ReconnectPauseUtility.Check(
            match.GetPauseReason(),
            TimeoutUtility.HasPauseToResume(
                match.GetCurrentMapStatus(),
                MatchUtility.Rules()?.MatchWaitingForResume == true
            ),
            ReconnectPauseUtility.ShouldPause(matchData, connected, match.GetExpectedPlayerCount()),
            ShouldRequireTeamResume() || _cameraSystem.IsBlocking() || IsTimeoutActive(),
            MatchUtility.GetMemberFromLineup(
                matchData,
                player.SteamID.ToString(),
                player.PlayerName
            ) != null
        );

        if (check == eRosterCheck.NotPaused)
        {
            _gameServer.Message(
                HudDestination.Chat,
                _gameServer.Localize(player, "timeout.not_paused", ChatColors.Red),
                player
            );
            return;
        }

        if (check == eRosterCheck.Missing)
        {
            string missing = string.Join(
                ", ",
                ReadyUtility.EmptySeats(matchData, connected, match.GetExpectedPlayerCount())
            );

            _gameServer.MessageEach(
                HudDestination.Chat,
                viewer => _gameServer.Localize(viewer, "timeout.check_missing", missing)
            );
            return;
        }

        if (check == eRosterCheck.Whole)
        {
            _gameServer.MessageEach(
                HudDestination.Chat,
                viewer => _gameServer.Localize(viewer, "timeout.check_whole")
            );
            return;
        }

        string checkedBy = player.PlayerName;

        match.ResumeMatch(viewer =>
            _gameServer.Localize(viewer, "timeout.check_resumed", checkedBy, ChatColors.Red)
        );
    }

    // A LAN match waits for someone to resume it. Any other waits one technical
    // timeout at a time, each paid by a lineup that is short, and plays on once
    // they are spent.
    public bool HoldForRoster(IEnumerable<(string SteamId, string Name)> connected)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (match == null || matchData == null)
        {
            return false;
        }

        if (match.IsLan() || _reconnectTimeoutOwed || _reconnectTimer != null)
        {
            return true;
        }

        ePauseReason? held = match.GetPauseReason();

        if (held != null && held != ePauseReason.Roster)
        {
            return true;
        }

        List<(string SteamId, string Name)> players = connected.ToList();

        // A lineup with nobody left is waited on, at no cost: playing on would
        // give the other side rounds against no one.
        _waitsOnEmptyLineup = ReconnectPauseUtility.IsALineupEmpty(
            matchData,
            players,
            match.GetExpectedPlayerCount()
        );

        if (_waitsOnEmptyLineup)
        {
            return true;
        }

        if (!ChargeReconnectTimeout(match, matchData, players))
        {
            return false;
        }

        // The clock only starts once the pause lands: behind a tactical timeout
        // it is retried, and the player may be back before it ever does.
        if (held == ePauseReason.Roster)
        {
            StartReconnectTimer(match);
        }
        else
        {
            _reconnectTimeoutOwed = true;
        }

        return true;
    }

    public bool HoldsForRoster()
    {
        return _reconnectTimeoutOwed
            || _waitsOnEmptyLineup
            || _reconnectTimer != null
            || _matchService.GetCurrentMatch()?.IsLan() == true;
    }

    public void RosterPauseLanded()
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (!_reconnectTimeoutOwed || match == null)
        {
            return;
        }

        _reconnectTimeoutOwed = false;
        StartReconnectTimer(match);
    }

    public void RosterChanged(IEnumerable<(string SteamId, string Name)> connected)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (match == null || matchData == null)
        {
            return;
        }

        List<(string SteamId, string Name)> players = connected.ToList();
        bool shortHanded = ReconnectPauseUtility.ShouldPause(
            matchData,
            players,
            match.GetExpectedPlayerCount()
        );

        if (!shortHanded)
        {
            if (_reconnectTimeoutOwed)
            {
                _reconnectTimeouts.Refund();
            }

            _reconnectTimeoutOwed = false;
            _waitsOnEmptyLineup = false;
            return;
        }

        // Charged at the drop, before the pause landed: whoever is short now pays,
        // and a lineup with nobody left is still waited on at no cost.
        if (_reconnectTimeoutOwed)
        {
            _reconnectTimeouts.Refund();
            _waitsOnEmptyLineup = ReconnectPauseUtility.IsALineupEmpty(
                matchData,
                players,
                match.GetExpectedPlayerCount()
            );
            _reconnectTimeoutOwed =
                !_waitsOnEmptyLineup && ChargeReconnectTimeout(match, matchData, players);
            return;
        }

        if (
            !_waitsOnEmptyLineup
            || ReconnectPauseUtility.IsALineupEmpty(
                matchData,
                players,
                match.GetExpectedPlayerCount()
            )
        )
        {
            return;
        }

        if (RosterOwnsPause())
        {
            SettleRosterPause(match, matchData, players);
            return;
        }

        _waitsOnEmptyLineup = false;
        _reconnectTimeoutOwed =
            match.GetPauseReason() == null && ChargeReconnectTimeout(match, matchData, players);
    }

    public int? ReconnectTimeoutSecondsLeft()
    {
        if (_reconnectTimeoutEnds is not DateTime ends || !RosterOwnsPause())
        {
            return null;
        }

        return Math.Max(0, (int)Math.Ceiling((ends - DateTime.UtcNow).TotalSeconds));
    }

    private bool ChargeReconnectTimeout(
        MatchManager match,
        MatchData matchData,
        List<(string SteamId, string Name)> connected
    )
    {
        return TechnicalTimeoutSetting("mp_technical_timeout_duration_s", 120) > 0
            && _reconnectTimeouts.Charge(
                match.GetActiveMapId() ?? matchData.current_match_map_id,
                ReconnectPauseUtility.ShortLineups(
                    matchData,
                    connected,
                    match.GetExpectedPlayerCount()
                ),
                TechnicalTimeoutSetting("mp_technical_timeout_per_team", 2)
            );
    }

    private void StartReconnectTimer(MatchManager match)
    {
        int seconds = Math.Max(1, TechnicalTimeoutSetting("mp_technical_timeout_duration_s", 120));

        _reconnectTimeoutEnds = DateTime.UtcNow.AddSeconds(seconds);
        _reconnectTimer = match.PhaseTimer(seconds, EndReconnectTimeout);
    }

    private void EndReconnectTimeout()
    {
        StopReconnectTimer();

        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (match == null || matchData == null || !RosterOwnsPause())
        {
            return;
        }

        SettleRosterPause(
            match,
            matchData,
            MatchUtility.Players().Select(player => (player.SteamID.ToString(), player.PlayerName)).ToList()
        );
    }

    private void SettleRosterPause(
        MatchManager match,
        MatchData matchData,
        List<(string SteamId, string Name)> connected
    )
    {
        StopReconnectTimer();

        // ResumeMatch refuses during a tactical timeout or a round restore, and
        // nothing else would come back to a pause whose timeouts are spent.
        if (IsTimeoutActive() || _backUpManagement.IsResettingRound())
        {
            _reconnectTimer = match.PhaseTimer(1, EndReconnectTimeout);
            return;
        }

        _waitsOnEmptyLineup = ReconnectPauseUtility.IsALineupEmpty(
            matchData,
            connected,
            match.GetExpectedPlayerCount()
        );

        if (_waitsOnEmptyLineup)
        {
            return;
        }

        if (ChargeReconnectTimeout(match, matchData, connected))
        {
            StartReconnectTimer(match);
            return;
        }

        match.ResumeMatch(viewer => _gameServer.Localize(viewer, "match.reconnect_timeout_over"));
    }

    private void StopReconnectTimeout()
    {
        StopReconnectTimer();

        if (_reconnectTimeoutOwed)
        {
            _reconnectTimeouts.Refund();
        }

        _reconnectTimeoutOwed = false;
        _waitsOnEmptyLineup = false;
    }

    private void StopReconnectTimer()
    {
        if (_reconnectTimer != null)
        {
            _matchService.GetCurrentMatch()?.KillPhaseTimer(_reconnectTimer);
        }

        _reconnectTimer = null;
        _reconnectTimeoutEnds = null;
    }

    // Read by its own type: a float read as an int is a number in the billions.
    private int TechnicalTimeoutSetting(string name, int fallback)
    {
        try
        {
            ConVar? conVar = ConVar.Find(name);

            if (conVar == null)
            {
                return fallback;
            }

            switch (conVar.Type)
            {
                case ConVarType.Int32:
                    return conVar.GetPrimitiveValue<int>();
                case ConVarType.Float32:
                    return (int)conVar.GetPrimitiveValue<float>();
                default:
                    return fallback;
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, $"Could not read {name}, using {fallback}");

            return fallback;
        }
    }

    // Only meaningful once the match is actually being played -- during warmup
    // a team being empty is normal and resuming is how the match gets going.
    private bool IsATeamEmpty()
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null || (!match.IsInPlay() && !match.IsKnife()))
        {
            return false;
        }

        int ts = 0;
        int cts = 0;

        foreach (CCSPlayerController player in MatchUtility.Players())
        {
            if (player.Team == CsTeam.Terrorist)
            {
                ts++;
            }
            else if (player.Team == CsTeam.CounterTerrorist)
            {
                cts++;
            }
        }

        return ts == 0 || cts == 0;
    }

    private void PauseTechMatch(Func<CCSPlayerController?, string> pauseMessage)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null || ShouldRequireTeamResume() || !match.IsInPlay())
        {
            return;
        }

        resumeVote?.Abandon();
        resumeVote = null;

        _teamsPendingResume.Clear();
        _teamsPendingResume.Add(CsTeam.CounterTerrorist);
        _teamsPendingResume.Add(CsTeam.Terrorist);
        _requiresTeamResumeForCurrentPause = true;
        match.PauseMatch(ePauseReason.Tech, pauseMessage);
    }

    public void CallTacTimeout(CCSPlayerController? player)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null || !match.IsInPlay() || _backUpManagement.IsResettingRound())
        {
            _gameServer.Message(
                HudDestination.Chat,
                _gameServer.Localize(player, "timeout.cannot_tac_not_live", ChatColors.Red),
                player
            );
            return;
        }

        if (IsTimeoutActive())
        {
            SendTimeoutAlreadyActiveMessage(player);
            return;
        }

        if (player != null)
        {
            if (!CanCallTacticalTimeout(player))
            {
                CannotPauseMessage(player, "timeout.not_allowed.tactical_timeout");
                return;
            }

            // Read timeout count from CS2's native game rules
            var rules = MatchUtility.Rules();
            int timeoutsAvailable =
                player.Team == CsTeam.Terrorist
                    ? rules?.TerroristTimeOuts ?? 0
                    : rules?.CTTimeOuts ?? 0;

            if (timeoutsAvailable == 0)
            {
                _gameServer.Message(
                    HudDestination.Chat,
                    _gameServer.Localize(player, "timeout.no_timeouts_left"),
                    player
                );
                return;
            }

            // Let CS2 handle the timeout natively
            _gameServer.SendCommands([
                $"timeout_{(player.Team == CsTeam.Terrorist ? "terrorist" : "ct")}_start",
            ]);

            // After CS2 processes the timeout, sync state to DB
            Server.NextFrame(() =>
            {
                int remaining = timeoutsAvailable - 1;

                _gameServer.MessageEach(
                    HudDestination.Alert,
                    viewer => _gameServer.Localize(
                        viewer,
                        "timeout.called_tactical",
                        player.PlayerName,
                        ChatColors.Red,
                        remaining
                    )
                );

                PublishTimeoutState();
            });
        }
        else
        {
            _gameServer.MessageEach(
                HudDestination.Alert,
                viewer => _gameServer.Localize(viewer, "timeout.called_admin")
            );
        }
    }

    private eTimeoutSettings GetTechnicalPauseSetting()
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null || !match.IsInPlay() && _backUpManagement.IsResettingRound() == false)
        {
            return eTimeoutSettings.Admin;
        }

        MatchData? matchData = match.GetMatchData();

        if (matchData == null)
        {
            return eTimeoutSettings.Admin;
        }

        eTimeoutSettings timeoutSetting = TimeoutUtility.TimeoutSettingStringToEnum(
            matchData.options.tech_timeout_setting
        );

        return timeoutSetting;
    }

    private eTimeoutSettings GetTacticalTimeoutSetting()
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null || !match.IsInPlay() && _backUpManagement.IsResettingRound() == false)
        {
            return eTimeoutSettings.Admin;
        }

        MatchData? matchData = match.GetMatchData();

        if (matchData == null)
        {
            return eTimeoutSettings.Admin;
        }

        eTimeoutSettings timeoutSetting = TimeoutUtility.TimeoutSettingStringToEnum(
            matchData.options.timeout_setting
        );

        return timeoutSetting;
    }

    private bool IsAdminOrOrganizer(CCSPlayerController player, MatchData matchData)
    {
        return TimeoutUtility.IsStaff(
            ConnectedRole(player, matchData),
            LineupRole(player, matchData)
        );
    }

    private static ePlayerRoles? ConnectedRole(CCSPlayerController player, MatchData? matchData)
    {
        return FiveStackPlugin.ConnectRoles.RoleIn(player.SteamID, matchData?.id);
    }

    private static ePlayerRoles? LineupRole(CCSPlayerController player, MatchData? matchData)
    {
        if (matchData == null)
        {
            return null;
        }

        MatchMember? member = MatchUtility.GetMemberFromLineup(
            matchData,
            player.SteamID.ToString(),
            player.PlayerName
        );

        if (member == null)
        {
            return null;
        }

        return PlayerRoleUtility.PlayerRoleStringToEnum(member.role);
    }

    private void SendAlreadyPausedMessage(CCSPlayerController? player)
    {
        if (player == null)
        {
            return;
        }

        _gameServer.Message(
            HudDestination.Chat,
            _gameServer.Localize(player, "timeout.already_paused", ChatColors.Red),
            player
        );
    }

    private void SendTimeoutAlreadyActiveMessage(CCSPlayerController? player)
    {
        if (player == null)
        {
            return;
        }

        _gameServer.Message(
            HudDestination.Chat,
            _gameServer.Localize(player, "timeout.already_active", ChatColors.Red),
            player
        );
    }

    public bool IsTimeoutActive()
    {
        return MatchUtility.Rules()?.TerroristTimeOutActive == true
            || MatchUtility.Rules()?.CTTimeOutActive == true;
    }

    public (int lineup1Timeouts, int lineup2Timeouts) GetLineupTimeouts()
    {
        var rules = MatchUtility.Rules();
        int tTimeouts = rules?.TerroristTimeOuts ?? 0;
        int ctTimeouts = rules?.CTTimeOuts ?? 0;

        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();
        MatchMap? currentMap = match?.GetCurrentMap();

        if (matchData == null || currentMap == null)
        {
            return (0, 0);
        }

        int totalRoundsPlayed = _gameServer.GetTotalRoundsPlayed();

        CsTeam lineup1Side = TeamUtility.GetLineupSide(
            matchData,
            currentMap,
            matchData.lineup_1_id,
            totalRoundsPlayed
        );

        if (lineup1Side == CsTeam.Terrorist)
        {
            return (tTimeouts, ctTimeouts);
        }

        return (ctTimeouts, tTimeouts);
    }

    public void PublishTimeoutState()
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchMap? currentMap = match?.GetCurrentMap();

        if (currentMap == null)
        {
            return;
        }

        Guid mapId = match!.GetActiveMapId() ?? currentMap.id;

        (int lineup1Timeouts, int lineup2Timeouts) = GetLineupTimeouts();

        _matchEvents.PublishGameEvent(
            "techTimeout",
            new Dictionary<string, object>
            {
                { "map_id", mapId },
                { "lineup_1_timeouts_available", lineup1Timeouts },
                { "lineup_2_timeouts_available", lineup2Timeouts },
            }
        );
    }
}
