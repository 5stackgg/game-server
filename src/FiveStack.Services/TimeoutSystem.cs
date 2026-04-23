using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class TimeoutSystem
{
    private readonly HashSet<CsTeam> _teamsPendingResume = new();
    private bool _requiresTeamResumeForCurrentPause;
    private readonly MatchEvents _matchEvents;
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly GameBackUpRounds _backUpManagement;
    private readonly ILogger<TimeoutSystem> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly CoachSystem _coachSystem;
    private readonly CaptainSystem _captainSystem;
    private readonly IStringLocalizer _localizer;
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
        IStringLocalizer localizer
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
        _localizer = localizer;
    }

    public void RequestPause(CCSPlayerController? player)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null || !match.IsInProgress() || _backUpManagement.IsResettingRound())
        {
            _gameServer.Message(
                HudDestination.Chat,
                _localizer["timeout.cannot_pause_not_live", ChatColors.Red],
                player
            );
            return;
        }

        if (IsTimeoutActive())
        {
            SendTimeoutAlreadyActiveMessage(player);
            return;
        }

        string pauseMessage = _localizer["timeout.admin_paused"];

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
                        _localizer["timeout.vote.technical"],
                        new CsTeam[] { CsTeam.CounterTerrorist, CsTeam.Terrorist },
                        (
                            () =>
                            {
                                _logger.LogInformation("technical pause vote passed");
                                PauseTechMatch(_localizer["timeout.vote.technical_passed"]);
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

            pauseMessage = _localizer["timeout.player_paused", player.PlayerName, ChatColors.Red];
        }

        PauseTechMatch(pauseMessage);
    }

    private bool CanPause(CCSPlayerController? player)
    {
        if (player == null)
        {
            return true;
        }

        bool isCoach = _coachSystem.IsCoach(player, player.Team);
        bool isCaptain = _captainSystem.IsCaptain(player, player.Team);

        if (
            player.Clan == "[administrator]"
            || player.Clan == "[match_organizer]"
            || player.Clan == "[tournament_organizer]"
        )
        {
            return true;
        }

        switch (GetTechnicalPauseSetting())
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
                MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();

                if (matchData == null)
                {
                    return false;
                }

                MatchMember? lineupPlayer = MatchUtility.GetMemberFromLineup(
                    matchData,
                    player.SteamID.ToString(),
                    player.PlayerName
                );

                if (lineupPlayer == null)
                {
                    return false;
                }

                var roleEnum = PlayerRoleUtility.PlayerRoleStringToEnum(lineupPlayer.role);
                if (
                    roleEnum == ePlayerRoles.Administrator
                    || roleEnum == ePlayerRoles.MatchOrganizer
                    || roleEnum == ePlayerRoles.TournamentOrganizer
                )
                {
                    return true;
                }

                return false;
        }

        return true;
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

    private void CannotPauseMessage(CCSPlayerController? player, string type)
    {
        _gameServer.Message(
            HudDestination.Chat,
            _localizer["timeout.not_allowed", ChatColors.Red, type],
            player
        );
    }

    public void RequestResume(CCSPlayerController? player)
    {
        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();

        if (matchData == null)
        {
            return;
        }

        string resumeMessage = _localizer["timeout.admin_resumed"];

        if (player != null)
        {
            if (ShouldRequireTeamResume())
            {
                if (IsAdminOrOrganizer(player, matchData))
                {
                    ClearPendingTeamResumes();
                    _matchService.GetCurrentMatch()?.ResumeMatch(resumeMessage);
                    return;
                }

                if (!CanPause(player))
                {
                    CannotPauseMessage(player, "resume");
                    return;
                }

                if (_teamsPendingResume.Contains(player.Team) == false)
                {
                    _gameServer.Message(
                        HudDestination.Chat,
                        $" {ChatColors.Red}Your team has already resumed. Waiting for the other team.",
                        player
                    );
                    return;
                }

                _teamsPendingResume.Remove(player.Team);
                if (ShouldRequireTeamResume())
                {
                    _gameServer.Message(
                        HudDestination.Alert,
                        $"{player.PlayerName} {ChatColors.Red}resumed for {player.Team}. Waiting for the other team to resume."
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
                        _localizer["timeout.vote.resume"],
                        new CsTeam[] { CsTeam.CounterTerrorist, CsTeam.Terrorist },
                        (
                            () =>
                            {
                                _logger.LogInformation("resume vote passed");
                                _matchService
                                    .GetCurrentMatch()
                                    ?.ResumeMatch(_localizer["timeout.vote.resume_passed"]);
                                resumeVote = null;
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

            resumeMessage = _localizer["timeout.player_resumed", player.PlayerName, ChatColors.Red];
        }

        ClearPendingTeamResumes();
        _matchService.GetCurrentMatch()?.ResumeMatch(resumeMessage);
    }

    public void ClearPendingTeamResumes()
    {
        _teamsPendingResume.Clear();
        _requiresTeamResumeForCurrentPause = false;
    }

    private bool ShouldRequireTeamResume()
    {
        return _requiresTeamResumeForCurrentPause && _teamsPendingResume.Count > 0;
    }

    private void PauseTechMatch(string pauseMessage)
    {
        _teamsPendingResume.Clear();
        _teamsPendingResume.Add(CsTeam.CounterTerrorist);
        _teamsPendingResume.Add(CsTeam.Terrorist);
        _requiresTeamResumeForCurrentPause = true;
        _matchService.GetCurrentMatch()?.PauseMatch(pauseMessage);
    }

    public void CallTacTimeout(CCSPlayerController? player)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null || !match.IsInProgress() || _backUpManagement.IsResettingRound())
        {
            _gameServer.Message(
                HudDestination.Chat,
                _localizer["timeout.cannot_tac_not_live", ChatColors.Red],
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
                CannotPauseMessage(player, "tactical timeout");
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
                    _localizer["timeout.no_timeouts_left"],
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

                _gameServer.Message(
                    HudDestination.Alert,
                    _localizer[
                        "timeout.called_tactical",
                        player.PlayerName,
                        ChatColors.Red,
                        remaining
                    ]
                );

                PublishTimeoutState();
            });
        }
        else
        {
            _gameServer.Message(HudDestination.Alert, _localizer["timeout.called_admin"]);
        }
    }

    private eTimeoutSettings GetTechnicalPauseSetting()
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null || !match.IsInProgress() && _backUpManagement.IsResettingRound() == false)
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

        if (match == null || !match.IsInProgress() && _backUpManagement.IsResettingRound() == false)
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
        if (
            player.Clan == "[administrator]"
            || player.Clan == "[match_organizer]"
            || player.Clan == "[tournament_organizer]"
        )
        {
            return true;
        }

        MatchMember? lineupPlayer = MatchUtility.GetMemberFromLineup(
            matchData,
            player.SteamID.ToString(),
            player.PlayerName
        );

        if (lineupPlayer == null)
        {
            return false;
        }

        var roleEnum = PlayerRoleUtility.PlayerRoleStringToEnum(lineupPlayer.role);
        return roleEnum == ePlayerRoles.Administrator
            || roleEnum == ePlayerRoles.MatchOrganizer
            || roleEnum == ePlayerRoles.TournamentOrganizer;
    }

    private void SendTimeoutAlreadyActiveMessage(CCSPlayerController? player)
    {
        if (player == null)
        {
            return;
        }

        _gameServer.Message(
            HudDestination.Chat,
            _localizer["timeout.already_active", ChatColors.Red],
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
        Guid? loadedMapId = match?.GetLoadedMapIdForEvents();

        if (currentMap == null || loadedMapId == null)
        {
            return;
        }

        (int lineup1Timeouts, int lineup2Timeouts) = GetLineupTimeouts();

        _matchEvents.PublishGameEvent(
            "techTimeout",
            new Dictionary<string, object>
            {
                { "map_id", loadedMapId.Value },
                { "lineup_1_timeouts_available", lineup1Timeouts },
                { "lineup_2_timeouts_available", lineup2Timeouts },
            }
        );
    }
}
