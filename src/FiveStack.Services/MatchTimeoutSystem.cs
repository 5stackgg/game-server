using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class MatchTimeoutSystem
{
    private readonly GameEvents _gameEvents;
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly BackUpManagement _backUpManagement;
    private readonly ILogger<MatchTimeoutSystem> _logger;

    public MatchTimeoutSystem(
        ILogger<MatchTimeoutSystem> logger,
        GameEvents gameEvents,
        GameServer gameServer,
        MatchService matchService,
        BackUpManagement backUpManagement
    )
    {
        _logger = logger;
        _gameEvents = gameEvents;
        _gameServer = gameServer;
        _matchService = matchService;
        _backUpManagement = backUpManagement;
    }

    public void CallTimeout(CCSPlayerController? player)
    {
        FiveStackMatch? match = _matchService.GetMatchData();

        if (match == null || _matchService.IsLive() && _backUpManagement.IsResttingRound() == false)
        {
            return;
        }

        _gameServer.SendCommands(new[] { "mp_pause_match" });

        string pauseMessage = "Admin Paused the Match";

        if (player != null)
        {
            eTimeoutSettings timeoutSetting = TimeoutUtility.TimeoutSettingStringToEnum(
                match.tech_timeout_setting
            );

            // TODO - coach support
            if (timeoutSetting != eTimeoutSettings.CoachAndPlayers)
            {
                _gameServer.Message(
                    HudDestination.Chat,
                    $" {ChatColors.Red}you are not allowed to pause the match!",
                    player
                );
                return;
            }

            pauseMessage = $"{player.PlayerName} {ChatColors.Red}paused the match";
        }

        _gameServer.Message(HudDestination.Alert, pauseMessage);

        _matchService.UpdateMapStatus(eMapStatus.Paused);
    }

    public void Resume(CCSPlayerController? player)
    {
        FiveStackMatch? match = _matchService.GetMatchData();
        if (match == null || _matchService.IsLive() || _backUpManagement.IsResttingRound())
        {
            return;
        }

        _gameServer.SendCommands(new[] { "mp_unpause_match" });

        string pauseMessage = "Admin Resumed the Match";

        if (player != null)
        {
            eTimeoutSettings timeoutSetting = TimeoutUtility.TimeoutSettingStringToEnum(
                match.tech_timeout_setting
            );

            // TODO - coach support
            if (timeoutSetting != eTimeoutSettings.CoachAndPlayers)
            {
                _gameServer.Message(
                    HudDestination.Chat,
                    $" {ChatColors.Red}you are not allowed to resume the match!",
                    player
                );
                return;
            }

            pauseMessage = $"{player.PlayerName} {ChatColors.Red}resumed the match";
        }

        _gameServer.Message(HudDestination.Alert, pauseMessage);

        _matchService.UpdateMapStatus(
            _matchService.isOverTime() ? eMapStatus.Overtime : eMapStatus.Live
        );
    }

    public void CallTacTimeout(CCSPlayerController? player)
    {
        MatchMap? currentMap = _matchService.GetCurrentMap();
        FiveStackMatch? match = _matchService.GetMatchData();

        if (
            match == null
            || currentMap == null
            || _matchService.IsLive() && _backUpManagement.IsResttingRound() == false
        )
        {
            return;
        }

        if (player != null)
        {
            eTimeoutSettings timeoutSetting = TimeoutUtility.TimeoutSettingStringToEnum(
                match.tech_timeout_setting
            );

            // TODO - coach support
            if (timeoutSetting != eTimeoutSettings.CoachAndPlayers)
            {
                _gameServer.Message(
                    HudDestination.Chat,
                    $" {ChatColors.Red}you are not allowed to call a tech timeout!",
                    player
                );
                return;
            }

            Guid? lineup_id = MatchUtility.GetPlayerLineup(match, player);

            if (lineup_id == null)
            {
                _logger.LogWarning("Unable to find player in lineup");
                return;
            }

            int timeouts_available =
                match.lineup_1_id == lineup_id
                    ? currentMap.lineup_1_timeouts_available
                    : currentMap.lineup_2_timeouts_available;

            if (timeouts_available == 0)
            {
                _gameServer.Message(
                    HudDestination.Chat,
                    $"Your team has used all its timeouts!",
                    player
                );
                return;
            }

            if (match.lineup_1_id == lineup_id)
            {
                currentMap.lineup_1_timeouts_available--;
            }
            else
            {
                currentMap.lineup_2_timeouts_available--;
            }

            timeouts_available--;

            CsTeam team = TeamUtility.TeamNumToCSTeam(player.TeamNum);

            _gameServer.SendCommands(
                new[] { $"timeout_{(team == CsTeam.Terrorist ? "terrorist" : "ct")}_start" }
            );

            _gameServer.Message(
                HudDestination.Alert,
                $"{player.PlayerName} {ChatColors.Red}called a tactical timeout ({timeouts_available} remaining)"
            );

            _gameEvents.PublishGameEvent(
                match.id,
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
            _gameServer.Message(HudDestination.Alert, "Tech Timeout Called by Admin");
        }

        _matchService.UpdateMapStatus(eMapStatus.TechTimeout);
    }
}
