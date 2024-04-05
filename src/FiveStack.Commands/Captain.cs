using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_captain", "Shows / Claims Captain Spots")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnCaptain(CCSPlayerController? player, CommandInfo? command)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (player == null || match == null)
        {
            return;
        }

        match.captainSystem.ShowCaptains();
        // match.captainSystem.ClaimCaptain(player);
    }

    [ConsoleCommand("css_release-captain", "Release Captain Spot")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnReleaseCaptain(CCSPlayerController? player, CommandInfo? command)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (player == null || match == null)
        {
            return;
        }

        match.captainSystem.RemoveCaptain(player);
    }
}
