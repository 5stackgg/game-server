using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

public partial class FiveStackPlugin
{
    public static readonly string ModelPathCtmSas = "agents\\models\\ctm_sas\\ctm_sas.vmdl";
    public static readonly string ModelPathTmPhoenix =
        "agents\\models\\tm_phoenix\\tm_phoenix.vmdl";

    [GameEventHandler(HookMode.Post)]
    public HookResult OnEventPlayerSpawn(EventPlayerSpawn @event)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (
            @event.UserIdPlayer == null
            || !@event.UserIdPlayer.IsValid
            || @event.UserIdPlayer.IsFakeClient
            || match == null
        )
        {
            return HookResult.Continue;
        }

        if ((match.GetMatchData()?.options.default_models ?? false) == false)
        {
            return HookResult.Continue;
        }

        IPlayer player = @event.UserIdPlayer;

        if (
            player == null
            || !player.IsValid
            || player.PlayerPawn == null
            || !player.PlayerPawn.IsValid
        )
        {
            return HookResult.Continue;
        }

        try
        {
            Team team =
                player.Controller.PendingTeamNum != player.Controller.TeamNum
                    ? (Team)player.Controller.PendingTeamNum
                    : (Team)player.Controller.TeamNum;

            if ((Team)player.Controller.TeamNum == Team.CT)
            {
                SetModelNextServerFrame(player.PlayerPawn, ModelPathCtmSas);
            }
            if ((Team)player.Controller.TeamNum == Team.T)
            {
                SetModelNextServerFrame(player.PlayerPawn, ModelPathTmPhoenix);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not set player model");
        }
        return HookResult.Continue;
    }

    public static void SetModelNextServerFrame(CCSPlayerPawn playerPawn, string model)
    {
        MatchUtility.Core.Scheduler.NextTick(() =>
        {
            playerPawn.SetModel(model);
        });
    }
}
