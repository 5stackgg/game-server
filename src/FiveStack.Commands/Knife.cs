using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_stay", "")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnStay(CCSPlayerController? player, CommandInfo? command)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (player == null || match == null)
        {
            return;
        }

        match.knifeSystem.Stay(player);
    }

    [ConsoleCommand("css_swap", "")]
    [ConsoleCommand("css_switch", "")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnSwitch(CCSPlayerController? player, CommandInfo? command)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (player == null || match == null)
        {
            return;
        }

        match.knifeSystem.Switch(player);
    }

    [ConsoleCommand("skip_knife", "Skips knife round")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnSkipKnife(CCSPlayerController? player, CommandInfo? command)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null)
        {
            return;
        }

        match.knifeSystem.Skip();
    }

    [ConsoleCommand(
        "api_knife_switch",
        "Should only be called by the API, this is so we know the api regonized the switch"
    )]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnApiSwitch(CCSPlayerController player, CommandInfo command)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null)
        {
            return;
        }

        match.knifeSystem.ConfirmSwitch();
    }
}
