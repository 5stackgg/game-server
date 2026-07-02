using SwiftlyS2.Shared.Commands;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [Command("tech", registerRaw: true, permission: "")]
    [CommandAlias("pause", registerRaw: true)]
    [CommandAlias("p", registerRaw: true)]
    public void OnPause(ICommandContext context)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match != null && match.gameEnded)
        {
            return;
        }

        match?.timeoutSystem.RequestPause(context.Sender);
    }

    [Command("resume", registerRaw: true, permission: "")]
    [CommandAlias("unpause", registerRaw: true)]
    [CommandAlias("up", registerRaw: true)]
    public void OnResume(ICommandContext context)
    {
        _matchService.GetCurrentMatch()?.timeoutSystem.RequestResume(context.Sender);
    }

    [Command("tac", registerRaw: true, permission: "")]
    [CommandAlias("timeout", registerRaw: true)]
    public void OnTimeout(ICommandContext context)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match != null && match.gameEnded)
        {
            return;
        }

        match?.timeoutSystem.CallTacTimeout(context.Sender);
    }
}
