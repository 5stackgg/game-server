using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    public static readonly string ModelPathCtmSas = "characters\\models\\ctm_sas\\ctm_sas.vmdl";
    public static readonly string ModelPathTmPhoenix =
        "characters\\models\\tm_phoenix\\tm_phoenix.vmdl";

    [GameEventHandler]
    public HookResult OnEventPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (@event.Userid == null || !@event.Userid.IsValid || @event.Userid.IsBot || match == null)
        {
            return HookResult.Continue;
        }

        if (match.GetMatchData()?.options.default_models == false)
        {
            return HookResult.Continue;
        }

        _logger.LogInformation("Default skins are enabled, skipping spawn event");

        CCSPlayerController player = @event.Userid;

        if (
            player == null
            || !player.IsValid
            || player.PlayerPawn == null
            || !player.PlayerPawn.IsValid
            || player.PlayerPawn.Value == null
            || !player.PlayerPawn.Value.IsValid
        )
        {
            return HookResult.Continue;
        }

        try
        {
            // TODO: Server crash if player connects, mp_swapteams and reconnect
            CsTeam team =
                player.PendingTeamNum != player.TeamNum
                    ? (CsTeam)player.PendingTeamNum
                    : (CsTeam)player.TeamNum;

            if ((CsTeam)player.TeamNum == CsTeam.CounterTerrorist)
            {
                SetModelNextServerFrame(player.PlayerPawn.Value, ModelPathCtmSas);
            }
            if ((CsTeam)player.TeamNum == CsTeam.Terrorist)
            {
                SetModelNextServerFrame(player.PlayerPawn.Value, ModelPathTmPhoenix);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Could not set player model: {0}", ex);
        }
        return HookResult.Continue;
    }

    public static void SetModelNextServerFrame(CCSPlayerPawn playerPawn, string model)
    {
        Server.NextFrame(() =>
        {
            playerPawn.SetModel(model);
        });
    }
}
