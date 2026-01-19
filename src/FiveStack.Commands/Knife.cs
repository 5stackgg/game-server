using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

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

    [ConsoleCommand("css_t", "")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnT(CCSPlayerController? player, CommandInfo? command)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (player == null || match == null)
        {
            return;
        }

        CsTeam winningTeam = match.knifeSystem.GetWinningTeam() ?? CsTeam.None;

        if (player.Team != winningTeam)
        {
            return;
        }

        if (winningTeam == CsTeam.Terrorist)
        {
            match.knifeSystem.Stay(player);
            return;
        }

        match.knifeSystem.Switch(player);
    }

    [ConsoleCommand("css_ct", "")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnCT(CCSPlayerController? player, CommandInfo? command)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (player == null || match == null)
        {
            return;
        }

        CsTeam winningTeam = match.knifeSystem.GetWinningTeam() ?? CsTeam.None;

        if (player.Team != winningTeam)
        {
            return;
        }

        if (winningTeam == CsTeam.CounterTerrorist)
        {
            match.knifeSystem.Stay(player);
            return;
        }

        match.knifeSystem.Switch(player);
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
