using System.Runtime.InteropServices;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    public MemoryFunctionVoid<CPlayer_ObserverServices> SpectatorChanged =
        new(_sepectatorSignature);

    private static readonly string _sepectatorSignature = RuntimeInformation.IsOSPlatform(
        OSPlatform.Linux
    )
        ? @"\x55\x48\x89\xE5\x41\x57\x41\x56\x41\x55\x4C\x8D\x2D\x2A\x2A\x2A\x2A\x41\x54\x49\x89\xFC\x53\x48\x89\xF3"
        // TODO - get for windows
        : @"";

    private void _watchSpectatorChanges()
    {
        // TODO - crashes service
        return;
        // SpectatorChanged.Hook(SpectatorChangedHook, HookMode.Post);
    }

    private HookResult SpectatorChangedHook(DynamicHook handle)
    {
        var observerServices = handle.GetParam<CPlayer_ObserverServices>(0);

        ObserverMode_t desiredMode = ObserverMode_t.OBS_MODE_IN_EYE;

        ulong? spectator = observerServices.Pawn.Value.Controller.Value?.SteamID;
        int viewingTeamNum = observerServices.ObserverTarget.Value?.TeamNum ?? -1;

        if (spectator == null || viewingTeamNum == -1)
        {
            return HookResult.Continue;
        }

        CsTeam viewingTeam = TeamUtility.TeamNumToCSTeam(viewingTeamNum);

        _logger.LogInformation(
            $"Specator ID: {spectator} is viewing {viewingTeam} @ {observerServices.ObserverMode}"
        );

        if (desiredMode != (ObserverMode_t)observerServices.ObserverMode)
        {
            _logger.LogWarning($"BAD SPECTATOR");
        }

        _logger.LogWarning($"BAD SPECTATOR2");

        if(viewingTeam == CsTeam.None || viewingTeam == CsTeam.Spectator || viewingTeam != CsTeam.Terrorist) {
            _logger.LogWarning("BAD VIEW");
            // return HookResult.Stop;
        }


        return HookResult.Continue;
    }
}
