using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [
        ConsoleCommand("css_r", "Toggles the player as ready"),
        ConsoleCommand("css_ready", "Toggles the player as ready"),
        ConsoleCommand("css_unready", "Toggles the player as ready"),
        ConsoleCommand("css_ur", "Toggles the player as ready")
    ]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnReady(CCSPlayerController? player, CommandInfo? command)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (player == null || match == null || !match.IsWarmup())
        {
            return;
        }

        match.readySystem.ToggleReady(player);
    }

    [ConsoleCommand("force_ready", "Forces the match to start")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnForceStart(CCSPlayerController? player, CommandInfo? command)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null)
        {
            return;
        }

        match.readySystem.Skip();
    }
}
