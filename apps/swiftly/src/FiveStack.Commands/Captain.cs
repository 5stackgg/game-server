using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [Command("captain", registerRaw: false, permission: "")]
    public void OnCaptain(ICommandContext context)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        IPlayer? player = context.Sender;

        if (player == null || match == null)
        {
            return;
        }

        match.captainSystem.ClaimCaptain(player, player.Controller.Team);
    }

    [Command("captains", registerRaw: false, permission: "")]
    public void OnShowCaptains(ICommandContext context)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        IPlayer? player = context.Sender;

        if (player == null || match == null)
        {
            return;
        }

        match.captainSystem.ShowCaptains();
    }

    [Command("release-captain", registerRaw: false, permission: "")]
    public void OnReleaseCaptain(ICommandContext context)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        IPlayer? player = context.Sender;

        if (player == null || match == null)
        {
            return;
        }

        match.captainSystem.RemoveCaptain(player);
    }
}
