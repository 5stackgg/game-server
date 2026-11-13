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
        bool isLineupMember =
            matchData != null
            && LineupMembership.LineupOf(matchData, player.SteamID.ToString(), player.Name) != null;

        if (!SurrenderSystem.CanCallSurrender(player.Controller.Team, isLineupMember))
        {
            player.SendConsole(" Only a player in this match can call a surrender");
            return;
        }

        _surrenderSystem.SetupSurrender(player.Controller.Team, player);
    }
}
