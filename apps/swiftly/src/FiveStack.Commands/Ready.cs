using FiveStack.Enums;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

public partial class FiveStackPlugin
{
    // During a halftime or overtime break .r readies the player's team.
    [Command("r", registerRaw: false, permission: "")]
    [CommandAlias("ready", registerRaw: false)]
    public void OnReady(ICommandContext context)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        IPlayer? player = context.Sender;

        if (player != null && match != null && match.timeoutSystem.IsBreak())
        {
            match.timeoutSystem.RequestResume(player, readyCommand: true);
            return;
        }

        ToggleWarmupReady(match, player);
    }

    [Command("unready", registerRaw: false, permission: "")]
    [CommandAlias("ur", registerRaw: false)]
    public void OnUnready(ICommandContext context)
    {
        ToggleWarmupReady(_matchService.GetCurrentMatch(), context.Sender);
    }

    private static void ToggleWarmupReady(MatchManager? match, IPlayer? player)
    {
        if (player == null || match == null || match.GetCurrentMapStatus() != eMapStatus.Warmup)
        {
            return;
        }

        match.readySystem.ToggleReady(player);
    }

    [Command("force_ready", registerRaw: true, permission: "")]
    public void OnForceStart(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            return;
        }

        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null)
        {
            return;
        }

        match.readySystem.Skip();
    }
}
