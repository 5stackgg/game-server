using FiveStack.Entities;
using FiveStack.Utilities;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Translation;

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

        ILocalizer localizer = _gameServer.LocalizerFor(context.Sender);

        context.Reply(localizer["help.available"]);

        context.Reply(localizer["help.toggle_ready", CommandUtility.PublicChatTrigger]);
        context.Reply(
            localizer[
                "help.knife_round",
                CommandUtility.PublicChatTrigger,
                CommandUtility.PublicChatTrigger
            ]
        );

        context.Reply(
            localizer[
                "help.captains",
                CommandUtility.SilentChatTrigger,
                CommandUtility.SilentChatTrigger
            ]
        );
        context.Reply(localizer["help.show_captains", CommandUtility.PublicChatTrigger]);

        context.Reply(localizer["help.tac_timeout", CommandUtility.SilentChatTrigger]);

        context.Reply(
            localizer[
                "help.tech_pause",
                CommandUtility.PublicChatTrigger,
                CommandUtility.PublicChatTrigger
            ]
        );

        context.Reply(localizer["help.rules", CommandUtility.PublicChatTrigger]);

        context.Reply(localizer["help.restore_round", CommandUtility.PublicChatTrigger]);
        context.Reply(localizer["help.stop", CommandUtility.PublicChatTrigger]);
    }

    [Command("rules", registerRaw: false, permission: "")]
    public void OnRules(ICommandContext context)
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();
        if (context.Sender == null || match == null)
        {
            return;
        }

        ILocalizer localizer = _gameServer.LocalizerFor(context.Sender);

        context.Reply(localizer["rules.title"]);
        string matchDetails = localizer[
            "rules.match_details",
            match.options.type,
            match.options.mr,
            match.options.best_of
        ];

        bool hasDetails = false;

        if (match.options.OvertimeEnabled())
        {
            hasDetails = true;
            matchDetails += $" {localizer["rules.with_overtime"]}";
        }

        if (match.options.KnifeRoundEnabled())
        {
            matchDetails +=
                $" {(hasDetails ? localizer["common.with"] : localizer["common.and"])} {localizer["rules.knife_round"]}";
        }

        context.Reply(matchDetails);

        hasDetails = false;
        string additionalDetails = "";

        if (match.options.coaches)
        {
            hasDetails = true;
            additionalDetails += $"{localizer["rules.coach_support"]}";
        }

        if (match.options.number_of_substitutes > 0)
        {
            additionalDetails +=
                $" {(hasDetails ? localizer["common.with"] : localizer["common.and"])} {localizer["rules.max_subs", match.options.number_of_substitutes]}";
        }

        if (additionalDetails != "")
        {
            context.Reply(additionalDetails);
        }

        context.Reply(
            localizer[
                "rules.allow_timeouts",
                TimeoutSetting(localizer, match.options.timeout_setting)
            ]
        );
        context.Reply(
            localizer[
                "rules.tech_timeouts",
                TimeoutSetting(localizer, match.options.tech_timeout_setting)
            ]
        );
    }

    private static string TimeoutSetting(ILocalizer localizer, string setting)
    {
        string? key = setting switch
        {
            "Coach" => "rules.timeouts.coach",
            "CoachAndCaptains" => "rules.timeouts.coach_and_captains",
            "CoachAndPlayers" => "rules.timeouts.coach_and_players",
            "Admin" => "rules.timeouts.admin",
            _ => null,
        };

        if (key == null)
        {
            return StringUtility.ConvertCamelToHumanReadable(setting);
        }

        return localizer[key];
    }
}
