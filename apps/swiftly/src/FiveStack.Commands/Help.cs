using FiveStack.Entities;
using FiveStack.Utilities;
using SwiftlyS2.Shared.Commands;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [Command("help", registerRaw: false, permission: "")]
    public void OnHelp(ICommandContext context)
    {
        if (context.Sender == null)
        {
            return;
        }

        context.Reply(_localizer["help.available"]);

        context.Reply(_localizer["help.toggle_ready", CommandUtility.PublicChatTrigger]);
        context.Reply(
            _localizer[
                "help.knife_round",
                CommandUtility.PublicChatTrigger,
                CommandUtility.PublicChatTrigger
            ]
        );

        context.Reply(
            _localizer[
                "help.captains",
                CommandUtility.SilentChatTrigger,
                CommandUtility.SilentChatTrigger
            ]
        );
        context.Reply(_localizer["help.show_captains", CommandUtility.PublicChatTrigger]);

        context.Reply(_localizer["help.tac_timeout", CommandUtility.SilentChatTrigger]);

        context.Reply(
            _localizer[
                "help.tech_pause",
                CommandUtility.PublicChatTrigger,
                CommandUtility.PublicChatTrigger
            ]
        );

        context.Reply(_localizer["help.rules", CommandUtility.PublicChatTrigger]);

        context.Reply(_localizer["help.restore_round", CommandUtility.PublicChatTrigger]);
    }

    [Command("rules", registerRaw: false, permission: "")]
    public void OnRules(ICommandContext context)
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();
        if (context.Sender == null || match == null)
        {
            return;
        }

        context.Reply(_localizer["rules.title"]);
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

        context.Reply(matchDetails);

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
            context.Reply(additionalDetails);
        }

        context.Reply(
            _localizer[
                "rules.allow_timeouts",
                StringUtility.ConvertCamelToHumanReadable(match.options.timeout_setting)
            ]
        );
        context.Reply(
            _localizer[
                "rules.tech_timeouts",
                StringUtility.ConvertCamelToHumanReadable(match.options.tech_timeout_setting)
            ]
        );
    }
}
