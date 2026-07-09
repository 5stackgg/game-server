using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [Command("stay", registerRaw: true, permission: "")]
    public void OnStay(ICommandContext context)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        IPlayer? player = context.Sender;

        if (player == null || match == null)
        {
            return;
        }

        match.knifeSystem.Stay(player);
    }

    [Command("swap", registerRaw: true, permission: "")]
    [CommandAlias("switch", registerRaw: true)]
    public void OnSwitch(ICommandContext context)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        IPlayer? player = context.Sender;

        if (player == null || match == null)
        {
            return;
        }

        match.knifeSystem.Switch(player);
    }

    [Command("skip_knife", registerRaw: true, permission: "")]
    public void OnSkipKnife(ICommandContext context)
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

        match.knifeSystem.Skip();
    }

    [Command("t", registerRaw: true, permission: "")]
    public void OnT(ICommandContext context)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        IPlayer? player = context.Sender;

        if (player == null || match == null)
        {
            return;
        }

        Team winningTeam = match.knifeSystem.GetWinningTeam() ?? Team.None;

        if (player.Controller.Team != winningTeam)
        {
            return;
        }

        if (winningTeam == Team.T)
        {
            match.knifeSystem.Stay(player);
            return;
        }

        match.knifeSystem.Switch(player);
    }

    [Command("ct", registerRaw: true, permission: "")]
    public void OnCT(ICommandContext context)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        IPlayer? player = context.Sender;

        if (player == null || match == null)
        {
            return;
        }

        Team winningTeam = match.knifeSystem.GetWinningTeam() ?? Team.None;

        if (player.Controller.Team != winningTeam)
        {
            return;
        }

        if (winningTeam == Team.CT)
        {
            match.knifeSystem.Stay(player);
            return;
        }

        match.knifeSystem.Switch(player);
    }

    [Command("api_knife_switch", registerRaw: true, permission: "")]
    public void OnApiSwitch(ICommandContext context)
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

        match.knifeSystem.ConfirmSwitch();
    }
}
