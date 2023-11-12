using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace PlayCs;

public partial class PlayCsPlugin
{
    [ConsoleCommand("css_help", "Shows Available Commands")]
    [ConsoleCommand("css_playcs", "Shows Available Commands")]
    [ConsoleCommand("css_commands", "Shows Available Commands")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnHelp(CCSPlayerController? player, CommandInfo? command)
    {
        if (player == null)
        {
            return;
        }

        command.ReplyToCommand($"  Available Commands: ");
        command.ReplyToCommand(
            $" {ChatColors.BlueGrey}Ready: {ChatColors.Default}!ready, !not-ready"
        );
        command.ReplyToCommand($" {ChatColors.BlueGrey}Timeouts: {ChatColors.Default}!timeout");
        command.ReplyToCommand(
            $" {ChatColors.BlueGrey}Knife Round: {ChatColors.Default}!stay, !switch"
        );
        command.ReplyToCommand(
            $" {ChatColors.BlueGrey}Captains: {ChatColors.Default}!captain, !release-captain"
        );
        command.ReplyToCommand(
            $" {ChatColors.BlueGrey}Tech Pause: {ChatColors.Default}!pause, !resume"
        );
    }
}
