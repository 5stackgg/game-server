using FiveStack.Utilities;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Translation;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [Command("y", registerRaw: false, permission: "")]
    public void OnVoteYes(ICommandContext context)
    {
        CastVoteAnswer(context, true);
    }

    [Command("n", registerRaw: false, permission: "")]
    public void OnVoteNo(ICommandContext context)
    {
        CastVoteAnswer(context, false);
    }

    private void CastVoteAnswer(ICommandContext context, bool answer)
    {
        IPlayer? player = context.Sender;

        if (player == null)
        {
            return;
        }

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
            context.Args.Length > 0 ? context.Args[0] : null
        );

        if (choices.Count > 0)
        {
            string reply = answer ? "y" : "n";
            _gameServer.Message(
                MessageType.Chat,
                $" More than one vote is open, answer one: {string.Join(" or ", choices.Select(choice => $"{CommandUtility.PublicChatTrigger}{reply} {choice}"))}",
                player
            );
            return;
        }

        foreach (string target in targets)
        {
            open[target]?.CastVote(player, answer);
        }
    }
}
