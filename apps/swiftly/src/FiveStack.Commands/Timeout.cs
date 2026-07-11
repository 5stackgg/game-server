using SwiftlyS2.Shared.Commands;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [Command("tech", registerRaw: false, permission: "")]
    [CommandAlias("pause", registerRaw: false)]
    [CommandAlias("p", registerRaw: false)]
    public void OnPause(ICommandContext context)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match != null && match.gameEnded)
        {
            return;
        }

        match?.timeoutSystem.RequestPause(context.Sender);
    }

    [Command("resume", registerRaw: false, permission: "")]
    [CommandAlias("unpause", registerRaw: false)]
    [CommandAlias("up", registerRaw: false)]
    public void OnResume(ICommandContext context)
    {
        _matchService.GetCurrentMatch()?.timeoutSystem.RequestResume(context.Sender);
    }

    [Command("tac", registerRaw: false, permission: "")]
    [CommandAlias("timeout", registerRaw: false)]
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
