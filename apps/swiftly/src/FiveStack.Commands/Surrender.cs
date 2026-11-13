using FiveStack.Entities;
using FiveStack.Utilities;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [Command("surrender", registerRaw: false, permission: "")]
    public void Surrender(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null)
        {
            return;
        }

        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();
        Guid? callerLineup =
            matchData == null
                ? null
                : LineupMembership.LineupOf(matchData, player.SteamID.ToString(), player.Name);

        if (!SurrenderSystem.CanCallSurrender(player.Controller.Team, callerLineup != null))
        {
            player.SendConsole(" Only a player in this match can call a surrender");
            return;
        }

        _surrenderSystem.SetupSurrender(player.Controller.Team, player, callerLineup);
    }
}
