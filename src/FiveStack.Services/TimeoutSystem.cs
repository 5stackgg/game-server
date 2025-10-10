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
        if (match == null || !match.IsLive() || _backUpManagement.IsResettingRound())
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
                if (pauseVote != null)
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
                                _matchService
                                    .GetCurrentMatch()
                                    ?.PauseMatch(_localizer["timeout.vote.technical_passed"]);
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

                    if (player != null)
                    {
                        pauseVote.CastVote(player, true);
                    }
                }

                return;
            }

            pauseMessage = _localizer["timeout.player_paused", player.PlayerName, ChatColors.Red];
        }

        _matchService.GetCurrentMatch()?.PauseMatch(pauseMessage);
    }

    private bool CanPause(CCSPlayerController? player)
    {
        if (player == null)
        {
            return true;
        }

        bool isCoach = _coachSystem.IsCoach(player, player.Team);
        bool isCaptain = _captainSystem.IsCaptain(player, player.Team);

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
            if (!CanPause(player))
            {
                if (resumeVote != null)
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

                    if (player != null)
                    {
                        resumeVote.CastVote(player, true);
                    }
                }

                return;
            }

            resumeMessage = _localizer["timeout.player_resumed", player.PlayerName, ChatColors.Red];
        }

        _matchService.GetCurrentMatch()?.ResumeMatch(resumeMessage);
    }

    public void CallTacTimeout(CCSPlayerController? player)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null || !match.IsLive() || _backUpManagement.IsResettingRound())
        {
            _gameServer.Message(
                HudDestination.Chat,
                _localizer["timeout.cannot_tac_not_live", ChatColors.Red],
                player
            );
            return;
        }

        MatchMap? currentMap = match.GetCurrentMap();
        MatchData? matchData = match.GetMatchData();

        if (matchData == null || currentMap == null)
        {
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

            Guid? lineup_id = MatchUtility.GetPlayerLineup(matchData, player);

            if (lineup_id == null)
            {
                _logger.LogWarning("Unable to find player in lineup");
                return;
            }

            int timeouts_available =
                matchData.lineup_1_id == lineup_id
                    ? currentMap.lineup_1_timeouts_available
                    : currentMap.lineup_2_timeouts_available;

            if (timeouts_available == 0)
            {
                _gameServer.Message(
                    HudDestination.Chat,
                    _localizer["timeout.no_timeouts_left"],
                    player
                );
                return;
            }

            if (matchData.lineup_1_id == lineup_id)
            {
                currentMap.lineup_1_timeouts_available--;
            }
            else
            {
                currentMap.lineup_2_timeouts_available--;
            }

            timeouts_available--;

            CallTimeout(player.Team);

            _gameServer.Message(
                HudDestination.Alert,
                _localizer[
                    "timeout.called_tactical",
                    player.PlayerName,
                    ChatColors.Red,
                    timeouts_available
                ]
            );

            _matchEvents.PublishGameEvent(
                "techTimeout",
                new Dictionary<string, object>
                {
                    { "map_id", currentMap.id },
                    { "lineup_1_timeouts_available", currentMap.lineup_1_timeouts_available },
                    { "lineup_2_timeouts_available", currentMap.lineup_2_timeouts_available },
                }
            );
        }
        else
        {
            _gameServer.Message(HudDestination.Alert, _localizer["timeout.called_admin"]);
        }
    }

    private void CallTimeout(CsTeam team)
    {
        _gameServer.SendCommands(
            new[] { $"timeout_{(team == CsTeam.Terrorist ? "terrorist" : "ct")}_start" }
        );

        Server.NextFrame(() =>
        {
            if (IsTimeoutActive())
            {
                return;
            }

            _logger.LogInformation($"Adding timeout for team {team}");

            _gameServer.SendCommands(
                new[] { $"mp_modify_timeouts {(team == CsTeam.Terrorist ? "T" : "CT")} 1" }
            );

            CallTimeout(team);
        });
    }

    private eTimeoutSettings GetTechnicalPauseSetting()
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null || !match.IsLive() && _backUpManagement.IsResettingRound() == false)
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

        if (match == null || !match.IsLive() && _backUpManagement.IsResettingRound() == false)
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
}
