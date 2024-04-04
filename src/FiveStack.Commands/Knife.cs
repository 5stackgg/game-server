using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_stay", "")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnStay(CCSPlayerController? player, CommandInfo? command)
    {
        MatchManager? match = CurrentMatch();

        if (player == null || match == null || !match.IsKnife())
        {
            return;
        }

        CsTeam? winningTeam = match.knifeSystem.GetWinningTeam();

        if (winningTeam == null)
        {
            return;
        }

        if (match.captainSystem.IsCaptain(player, winningTeam) == false)
        {
            _gameServer.Message(
                HudDestination.Chat,
                $" {ChatColors.Red}You are not the captain!",
                player
            );
            return;
        }

        _gameServer.Message(
            HudDestination.Alert,
            $"captain picked to {ChatColors.Red}stay {ChatColors.Default}sides"
        );

        match.UpdateMapStatus(eMapStatus.Live);
    }

    [ConsoleCommand("css_switch", "")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnSwitch(CCSPlayerController? player, CommandInfo? command)
    {
        MatchManager? match = CurrentMatch();

        if (player == null || match == null || !match.IsKnife())
        {
            return;
        }

        CsTeam? winningTeam = match.knifeSystem?.GetWinningTeam();

        if (winningTeam == null)
        {
            return;
        }

        if (match.captainSystem.IsCaptain(player, winningTeam) == false)
        {
            _gameServer.Message(
                HudDestination.Chat,
                $" {ChatColors.Red}You are not the captain!",
                player
            );
            return;
        }

        _gameServer.Message(
            HudDestination.Alert,
            $"captain picked to {ChatColors.Red}swap {ChatColors.Default}sides"
        );

        match.knifeSystem?.Switch();

        match.UpdateMapStatus(eMapStatus.Live);
    }

    [ConsoleCommand("skip_knife", "Skips knife round")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnSkipKnife(CCSPlayerController? player, CommandInfo? command)
    {
        MatchManager? match = CurrentMatch();

        if (player == null || match == null || !match.IsKnife())
        {
            return;
        }

        _gameServer.Message(HudDestination.Center, $"Skipping Knife.", player);

        match.UpdateMapStatus(eMapStatus.Live);
    }
}
