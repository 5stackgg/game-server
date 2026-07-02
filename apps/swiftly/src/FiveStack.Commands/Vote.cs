using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [Command("y", registerRaw: true, permission: "")]
    public void OnVoteYes(ICommandContext context)
    {
        CastVoteAnswer(context, true);
    }

    [Command("n", registerRaw: true, permission: "")]
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

        if (_gameBackupRounds.IsResettingRound())
        {
            _gameBackupRounds.restoreRoundVote?.CastVote(player, answer);
        }

        if (_surrenderSystem.IsSurrendering())
        {
            _surrenderSystem.surrenderingVote?.CastVote(player, answer);
        }

        TimeoutSystem? timeoutSystem = _matchService.GetCurrentMatch()?.timeoutSystem;

        if (timeoutSystem?.pauseVote != null)
        {
            timeoutSystem.pauseVote?.CastVote(player, answer);
        }

        if (timeoutSystem?.resumeVote != null)
        {
            timeoutSystem.resumeVote?.CastVote(player, answer);
        }
    }
}
