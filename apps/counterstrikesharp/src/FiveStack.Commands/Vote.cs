using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_n", "Answers no to the open vote")]
    [ConsoleCommand("css_y", "Answers yes to the open vote")]
    public void OnResetAnswer(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null)
        {
            return;
        }

        bool answer = command.ArgByIndex(0) == "css_y";

        TimeoutSystem? timeoutSystem = _matchService.GetCurrentMatch()?.timeoutSystem;

        Dictionary<string, VoteSystem?> open = new Dictionary<string, VoteSystem?>
        {
            ["restore"] = _gameBackupRounds.IsResettingRound()
                ? _gameBackupRounds.restoreRoundVote
                : null,
            ["surrender"] = _surrenderSystem.IsSurrendering()
                ? _surrenderSystem.surrenderingVote
                : null,
            ["pause"] = timeoutSystem?.pauseVote,
            ["resume"] = timeoutSystem?.resumeVote,
        };

        var (targets, choices) = VoteAnswerRouting.Route(
            open.Where(entry => entry.Value != null && entry.Value.IsVoteActive())
                .Select(entry => (entry.Key, entry.Value!.CanAnswer(player)))
                .ToList(),
            command.ArgCount > 1 ? command.ArgByIndex(1) : null
        );

        if (choices.Count > 0)
        {
            string reply = answer ? "y" : "n";
            player.PrintToChat(
                " "
                    + Localizer[
                        "vote.choose_one",
                        string.Join(
                            " / ",
                            choices.Select(choice =>
                                $"{CommandUtility.PublicChatTrigger}{reply} {choice}"
                            )
                        )
                    ]
            );
            return;
        }

        foreach (string target in targets)
        {
            open[target]?.CastVote(player, answer);
        }
    }
}
