using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_rules", "Shows Available Commands")]
    [ConsoleCommand("css_help", "Shows Available Commands")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnHelp(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null)
        {
            return;
        }

        command.ReplyToCommand($"  Available Commands: ");

        command.ReplyToCommand(
            $" {ChatColors.BlueGrey}Toggle Ready: {ChatColors.Default}{CommandUtility.PublicChatTrigger}r"
        );
        command.ReplyToCommand(
            $" {ChatColors.BlueGrey}Knife Round: {ChatColors.Default}{CommandUtility.PublicChatTrigger}stay, {CommandUtility.PublicChatTrigger}switch"
        );

        command.ReplyToCommand(
            $" {ChatColors.BlueGrey}Captains: {ChatColors.Default}{CommandUtility.SilentChatTrigger}captain, {CommandUtility.SilentChatTrigger}release-captain"
        );
        command.ReplyToCommand(
            $" {ChatColors.BlueGrey}Show Captains: {ChatColors.Default}{CommandUtility.PublicChatTrigger}captains"
        );

        command.ReplyToCommand(
            $" {ChatColors.BlueGrey}Tactical Timeout: {ChatColors.Default}{CommandUtility.SilentChatTrigger}tac"
        );

        command.ReplyToCommand(
            $" {ChatColors.BlueGrey}Technical Pause: {ChatColors.Default}{CommandUtility.PublicChatTrigger}tech, {CommandUtility.PublicChatTrigger}resume"
        );

        command.ReplyToCommand(
            $" {ChatColors.BlueGrey}Rules: {ChatColors.Default}{CommandUtility.PublicChatTrigger}rules"
        );

        command.ReplyToCommand(
            $" {ChatColors.BlueGrey}Restore Round: {ChatColors.Default}{CommandUtility.PublicChatTrigger}reset <round>"
        );
    }

    [ConsoleCommand("css_rules", "Shows Rules for Match")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnRules(CCSPlayerController? player, CommandInfo command)
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();
        if (player == null || match == null)
        {
            return;
        }

        command.ReplyToCommand($"  Rules: ");
        string matchDetails =
            $"{match.options.type} (MR:{match.options.mr}), Best of {match.options.best_of}";

        bool hasDetails = false;

        if (match.options.overtime)
        {
            hasDetails = true;
            matchDetails += $" with Overtime";
        }

        if (match.options.knife_round)
        {
            matchDetails += $"{(hasDetails ? " with" : " and")} knife round";
        }

        command.ReplyToCommand(matchDetails);

        hasDetails = false;
        string additionalDetails = "";

        if (match.options.coaches)
        {
            hasDetails = true;
            additionalDetails += $"Coach Support";
        }

        if (match.options.number_of_substitutes > 0)
        {
            additionalDetails +=
                $"{(hasDetails ? " with" : " and")} Maximum Number of Substitutes: {match.options.number_of_substitutes}";
        }

        if (additionalDetails != "")
        {
            command.ReplyToCommand(additionalDetails);
        }

        command.ReplyToCommand(
            $"Allow Timeouts {StringUtility.ConvertCamelToHumanReadable(match.options.timeout_setting)}"
        );
        command.ReplyToCommand(
            $"Tech Timeouts {StringUtility.ConvertCamelToHumanReadable(match.options.tech_timeout_setting)}"
        );
    }
}
