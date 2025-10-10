using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
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

        command.ReplyToCommand(_localizer["help.available"]);

        command.ReplyToCommand(_localizer["help.toggle_ready", CommandUtility.PublicChatTrigger]);
        command.ReplyToCommand(
            _localizer[
                "help.knife_round",
                CommandUtility.PublicChatTrigger,
                CommandUtility.PublicChatTrigger
            ]
        );

        command.ReplyToCommand(
            _localizer[
                "help.captains",
                CommandUtility.SilentChatTrigger,
                CommandUtility.SilentChatTrigger
            ]
        );
        command.ReplyToCommand(_localizer["help.show_captains", CommandUtility.PublicChatTrigger]);

        command.ReplyToCommand(_localizer["help.tac_timeout", CommandUtility.SilentChatTrigger]);

        command.ReplyToCommand(
            _localizer[
                "help.tech_pause",
                CommandUtility.PublicChatTrigger,
                CommandUtility.PublicChatTrigger
            ]
        );

        command.ReplyToCommand(_localizer["help.rules", CommandUtility.PublicChatTrigger]);

        command.ReplyToCommand(_localizer["help.restore_round", CommandUtility.PublicChatTrigger]);
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

        command.ReplyToCommand(_localizer["rules.title"]);
        string matchDetails = _localizer[
            "rules.match_details",
            match.options.type,
            match.options.mr,
            match.options.best_of
        ];

        bool hasDetails = false;

        if (match.options.overtime)
        {
            hasDetails = true;
            matchDetails += $" {_localizer["rules.with_overtime"]}";
        }

        if (match.options.knife_round)
        {
            matchDetails +=
                $" {(hasDetails ? _localizer["common.with"] : _localizer["common.and"])} {_localizer["rules.knife_round"]}";
        }

        command.ReplyToCommand(matchDetails);

        hasDetails = false;
        string additionalDetails = "";

        if (match.options.coaches)
        {
            hasDetails = true;
            additionalDetails += $"{_localizer["rules.coach_support"]}";
        }

        if (match.options.number_of_substitutes > 0)
        {
            additionalDetails +=
                $" {(hasDetails ? _localizer["common.with"] : _localizer["common.and"])} {_localizer["rules.max_subs", match.options.number_of_substitutes]}";
        }

        if (additionalDetails != "")
        {
            command.ReplyToCommand(additionalDetails);
        }

        command.ReplyToCommand(
            _localizer[
                "rules.allow_timeouts",
                StringUtility.ConvertCamelToHumanReadable(match.options.timeout_setting)
            ]
        );
        command.ReplyToCommand(
            _localizer[
                "rules.tech_timeouts",
                StringUtility.ConvertCamelToHumanReadable(match.options.tech_timeout_setting)
            ]
        );
    }
}
