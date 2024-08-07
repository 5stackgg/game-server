using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class Timeouts
{
    private readonly MatchEvents _matchEvents;
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly GameBackUpRounds _backUpManagement;
    private readonly ILogger<Timeouts> _logger;

    public Timeouts(
        ILogger<Timeouts> logger,
        MatchEvents matchEvents,
        GameServer gameServer,
        MatchService matchService,
        GameBackUpRounds backUpManagement
    )
    {
        _logger = logger;
        _matchEvents = matchEvents;
        _gameServer = gameServer;
        _matchService = matchService;
        _backUpManagement = backUpManagement;
    }

    public void CallTimeout(CCSPlayerController? player)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null || !match.IsLive() && _backUpManagement.IsResettingRound() == false)
        {
            return;
        }

        MatchData? matchData = match.GetMatchData();

        if (matchData == null)
        {
            return;
        }

        string pauseMessage = "Admin Paused the Match";

        if (player != null)
        {
            eTimeoutSettings timeoutSetting = TimeoutUtility.TimeoutSettingStringToEnum(
                matchData.options.tech_timeout_setting
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

        match.PauseMatch(pauseMessage);
    }

    public void Resume(CCSPlayerController? player)
    {
        if (player == null && _backUpManagement.IsResettingRound())
        {
            _backUpManagement.VoteFailed();
        }

        MatchManager? match = _matchService.GetCurrentMatch();

        // TODO - game rules has a bug where i cant detect if were paused
        if (match == null || _backUpManagement.IsResettingRound())
        {
            return;
        }

        MatchData? matchData = match.GetMatchData();

        if (matchData == null)
        {
            return;
        }

        _gameServer.SendCommands(new[] { "mp_unpause_match" });

        string pauseMessage = "Admin Resumed the Match";

        if (player != null)
        {
            eTimeoutSettings timeoutSetting = TimeoutUtility.TimeoutSettingStringToEnum(
                matchData.options.tech_timeout_setting
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
        match.UpdateMapStatus(match.isOverTime() ? eMapStatus.Overtime : eMapStatus.Live);
    }

    public void CallTacTimeout(CCSPlayerController? player)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null || !match.IsLive() || _backUpManagement.IsResettingRound())
        {
            return;
        }

        MatchMap? currentMap = match.GetCurrentMap();
        MatchData? matchData = match.GetMatchData();

        if (matchData == null || currentMap == null)
        {
            return;
        }

        if (player != null)
        {
            eTimeoutSettings timeoutSetting = TimeoutUtility.TimeoutSettingStringToEnum(
                matchData.options.tech_timeout_setting
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
                    $"Your team has used all its timeouts!",
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

            _gameServer.SendCommands(
                new[] { $"timeout_{(player.Team == CsTeam.Terrorist ? "terrorist" : "ct")}_start" }
            );

            _gameServer.Message(
                HudDestination.Alert,
                $"{player.PlayerName} {ChatColors.Red}called a tactical timeout ({timeouts_available} remaining)"
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
            _gameServer.Message(HudDestination.Alert, "Tech Timeout Called by Admin");
        }
    }
}
