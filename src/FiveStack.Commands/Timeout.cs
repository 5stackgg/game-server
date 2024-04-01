using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.enums;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_pause", "Pauses the match")]
    public void OnPause(CCSPlayerController? player, CommandInfo? command)
    {
        if (_matchData == null || !IsLive())
        {
            return;
        }

        SendCommands(new[] { "mp_pause_match" });

        string pauseMessage = "Admin Paused the Match";

        if (player != null)
        {
            eTimeoutSettings timeoutSetting = TimeoutSettingStringToEnum(
                _matchData.tech_timeout_setting
            );

            // TODO - coach support
            if (timeoutSetting != eTimeoutSettings.CoachAndPlayers)
            {
                Message(
                    HudDestination.Chat,
                    $" {ChatColors.Red}you are not allowed to pause the match!",
                    player
                );
                return;
            }

            pauseMessage = $"{player.PlayerName} {ChatColors.Red}paused the match";
        }

        Message(HudDestination.Alert, pauseMessage);

        UpdateMapStatus(eMapStatus.Paused);
    }

    [ConsoleCommand("css_resume", "Resumes the match")]
    public void OnResume(CCSPlayerController? player, CommandInfo? command)
    {
        if (_matchData == null || IsLive())
        {
            return;
        }

        SendCommands(new[] { "mp_unpause_match" });

        string pauseMessage = "Admin Resumed the Match";

        if (player != null)
        {
            eTimeoutSettings timeoutSetting = TimeoutSettingStringToEnum(
                _matchData.tech_timeout_setting
            );

            // TODO - coach support
            if (timeoutSetting != eTimeoutSettings.CoachAndPlayers)
            {
                Message(
                    HudDestination.Chat,
                    $" {ChatColors.Red}you are not allowed to resume the match!",
                    player
                );
                return;
            }

            pauseMessage = $"{player.PlayerName} {ChatColors.Red}resumed the match";
        }

        Message(HudDestination.Alert, pauseMessage);

        UpdateMapStatus(isOverTime() ? eMapStatus.Overtime : eMapStatus.Live);
    }

    [ConsoleCommand("css_tac", "Tactical Timeout")]
    public void OnTimeout(CCSPlayerController? player, CommandInfo? command)
    {
        if (_matchData == null || player == null || _currentMap == null || IsLive())
        {
            return;
        }

        if (player != null)
        {
            eTimeoutSettings timeoutSetting = TimeoutSettingStringToEnum(
                _matchData.tech_timeout_setting
            );

            // TODO - coach support
            if (timeoutSetting != eTimeoutSettings.CoachAndPlayers)
            {
                Message(
                    HudDestination.Chat,
                    $" {ChatColors.Red}you are not allowed to call a tech timeout!",
                    player
                );
                return;
            }

            Guid? lineup_id = GetPlayerLineup(player);

            if (lineup_id == null)
            {
                Logger.LogWarning("Unable to find player in lineup");
                return;
            }

            int timeouts_available =
                _matchData.lineup_1_id == lineup_id
                    ? _currentMap.lineup_1_timeouts_available
                    : _currentMap.lineup_2_timeouts_available;

            if (timeouts_available == 0)
            {
                Message(HudDestination.Chat, $"Your team has used all its timeouts!", player);
                return;
            }

            if (_matchData.lineup_1_id == lineup_id)
            {
                _currentMap.lineup_1_timeouts_available--;
            }
            else
            {
                _currentMap.lineup_2_timeouts_available--;
            }

            timeouts_available--;

            CsTeam team = TeamNumToCSTeam(player.TeamNum);

            SendCommands(
                new[] { $"timeout_{(team == CsTeam.Terrorist ? "terrorist" : "ct")}_start" }
            );

            Message(
                HudDestination.Alert,
                $"{player.PlayerName} {ChatColors.Red}called a tactical timeout ({timeouts_available} remaining)"
            );

            PublishGameEvent(
                "techTimeout",
                new Dictionary<string, object>
                {
                    { "map_id", _currentMap.id },
                    { "lineup_1_timeouts_available", _currentMap.lineup_1_timeouts_available },
                    { "lineup_2_timeouts_available", _currentMap.lineup_2_timeouts_available },
                }
            );
        }
        else
        {
            Message(HudDestination.Alert, "Tech Timeout Called by Admin");
        }

        UpdateMapStatus(eMapStatus.TechTimeout);
    }
}
