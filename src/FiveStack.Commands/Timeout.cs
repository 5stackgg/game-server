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
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnPause(CCSPlayerController? player, CommandInfo? command)
    {
        if (player == null || !IsLive())
        {
            return;
        }

        SendCommands(new[] { "mp_pause_match" });
        Message(HudDestination.Center, $"{player.PlayerName} {ChatColors.Red}paused the match");

        UpdateMapStatus(eMapStatus.Paused);
    }

    [ConsoleCommand("css_resume", "Resumes the match")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnResume(CCSPlayerController? player, CommandInfo? command)
    {
        if (player == null || IsLive())
        {
            return;
        }

        SendCommands(new[] { "mp_unpause_match" });
        Message(HudDestination.Center, $"{player.PlayerName} {ChatColors.Red}resumed the match");

        // TODO - detect if its in overtime
        UpdateMapStatus(eMapStatus.Live);
    }

    [ConsoleCommand("css_tac", "Tactical Timeout")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnTimeout(CCSPlayerController? player, CommandInfo? command)
    {
        if (_matchData == null || player == null || _currentMap == null || !IsLive())
        {
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

        SendCommands(new[] { $"timeout_{(team == CsTeam.Terrorist ? "terrorist" : "ct")}_start" });

        Message(
            HudDestination.Alert,
            $"{player.PlayerName} {ChatColors.Red}called a tactical timeout ({timeouts_available} remaining)"
        );

        UpdateMapStatus(eMapStatus.TechTimeout);
        _redis.PublishMatchEvent(
            _matchData.id,
            new Redis.EventData<Dictionary<string, object>>
            {
                @event = "techTimeout",
                data = new Dictionary<string, object>
                {
                    { "lineup_id", lineup_id },
                    { "map_id", _currentMap.id },
                }
            }
        );
    }
}
