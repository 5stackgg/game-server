using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_help", "Shows Available Commands")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnHelp(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null)
        {
            return;
        }

        command.ReplyToCommand($"  Available Commands: ");
        command.ReplyToCommand($" {ChatColors.BlueGrey}Toggle Ready: {ChatColors.Default}.r");
        command.ReplyToCommand(
            $" {ChatColors.BlueGrey}Knife Round: {ChatColors.Default}.stay, .switch"
        );
        command.ReplyToCommand(
            $" {ChatColors.BlueGrey}Captains: {ChatColors.Default}/captain, /release-captain"
        );

        command.ReplyToCommand($" {ChatColors.BlueGrey}Timeouts: {ChatColors.Default}/timeout");
        command.ReplyToCommand($" {ChatColors.BlueGrey}Tactical Pause: {ChatColors.Default}/tac");
        command.ReplyToCommand(
            $" {ChatColors.BlueGrey}Tech Pause: {ChatColors.Default}.pause, .resume"
        );
        command.ReplyToCommand($" {ChatColors.BlueGrey}Rules: {ChatColors.Default}.rules");
    }

    [ConsoleCommand("css_rules", "Shows Rules for Match")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnRules(CCSPlayerController? player, CommandInfo command)
    {
        FiveStackMatch? match = _matchService.GetCurrentMatch()?.GetMatchData();
        if (player == null || match == null)
        {
            return;
        }

        command.ReplyToCommand($"  Rules: ");
        string matchDetails = $"{match.type} (MR:{match.mr}), Best of {match.best_of}";

        bool hasDetails = false;

        if (match.overtime)
        {
            hasDetails = true;
            matchDetails += $" with Overtime";
        }

        if (match.knife_round)
        {
            matchDetails += $"{(hasDetails ? " with" : " and")} knife round";
        }

        command.ReplyToCommand(matchDetails);

        hasDetails = false;
        string additionalDetails = "";

        if (match.coaches)
        {
            hasDetails = true;
            additionalDetails += $"Coach Support";
        }

        if (match.number_of_substitutes > 0)
        {
            additionalDetails +=
                $"{(hasDetails ? " with" : " and")} Maximum Number of Substitutes: {match.number_of_substitutes}";
        }

        if (additionalDetails != "")
        {
            command.ReplyToCommand(additionalDetails);
        }

        command.ReplyToCommand(
            $"Allow Timeouts {StringUtility.ConvertCamelToHumanReadable(match.timeout_setting)}"
        );
        command.ReplyToCommand(
            $"Tech Timeouts {StringUtility.ConvertCamelToHumanReadable(match.tech_timeout_setting)}"
        );
    }
}
