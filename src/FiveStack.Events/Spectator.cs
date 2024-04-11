using System.Runtime.InteropServices;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    public MemoryFunctionVoid<CPlayer_ObserverServices, bool> SpectatorChanged =
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
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (matchData == null)
        {
            return HookResult.Continue;
        }

        var observerServices = handle.GetParam<CPlayer_ObserverServices>(0);

        ObserverMode_t desiredMode = ObserverMode_t.OBS_MODE_IN_EYE;

        CBasePlayerController? spectator = observerServices.Pawn.Value.Controller.Value;

        if (spectator == null)
        {
            return HookResult.Continue;
        }

        var player = CounterStrikeSharp.API.Utilities.GetPlayerFromSteamId(spectator.SteamID);

        int viewingTeamNum = observerServices.ObserverTarget.Value?.TeamNum ?? -1;

        if (player == null || viewingTeamNum == -1)
        {
            return HookResult.Continue;
        }

        CsTeam viewingTeam = TeamUtility.TeamNumToCSTeam(viewingTeamNum);

        _logger.LogInformation(
            $"Specator ID: {spectator.SteamID} is viewing {viewingTeam} @ {observerServices.ObserverMode}"
        );

        if (desiredMode != (ObserverMode_t)observerServices.ObserverMode)
        {
            _logger.LogWarning($"BAD SPECTATOR");
            // TODO - chnage their view to eyes
        }

        Guid? lineup_id = MatchUtility.GetPlayerLineup(matchData, player);

        if (lineup_id == null)
        {
            return HookResult.Continue;
        }

        CsTeam expectedTeam = CsTeam.None;

        string lineupName =
            matchData.lineup_1_id == lineup_id ? matchData.lineup_1.name : matchData.lineup_2.name;

        foreach (var team in MatchUtility.Teams())
        {
            if (team.ClanTeamname == lineupName)
            {
                expectedTeam = TeamUtility.TeamNumToCSTeam(team.TeamNum);
            }
        }

        if (expectedTeam == CsTeam.None)
        {
            _logger.LogWarning("Unable to get expected team");
            return HookResult.Continue;
        }

        if (viewingTeam != expectedTeam)
        {
            _logger.LogWarning("BAD VIEW");
            // TODO - we may be able todo this in PRE mode instead, otherwise we have to change it after they view that person
        }

        return HookResult.Continue;
    }
}
