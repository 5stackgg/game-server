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
    private static readonly string _specTatorChanged = RuntimeInformation.IsOSPlatform(
        OSPlatform.Linux
    )
        ? @"\x55\x48\x89\xE5\x41\x57\x41\x56\x41\x55\x4C\x8D\x2D\x2A\x2A\x2A\x2A\x41\x54\x49\x89\xFC\x53\x48\x89\xF3"
        // TODO - get for windows
        : @"";

    public MemoryFunctionWithReturn<CPlayer_ObserverServices, IntPtr, bool> SpectatorChanged =
        new(_specTatorChanged);

    private static readonly string _getNextObserverTarget = RuntimeInformation.IsOSPlatform(
        OSPlatform.Linux
    )
        ? @"\x55\x48\x89\xE5\x41\x57\x41\x56\x41\x55\x41\x89\xF5\x40\x0F\xB6\xF6"
        // TODO - get for windows
        : @"";

    public static MemoryFunctionWithReturn<
        CPlayer_ObserverServices,
        byte,
        bool
    > GetNextObserverTargetInputFunc = new(_getNextObserverTarget);

    public bool GetNextObserverTarget(CPlayer_ObserverServices services, byte unknownParam)
    {
        return GetNextObserverTargetInputFunc.Invoke(services, unknownParam);
    }

    private static readonly string _setNextObserveTarget = RuntimeInformation.IsOSPlatform(
        OSPlatform.Linux
    )
        ? @"\x55\x48\x89\xE5\x41\x55\x49\x89\xFD\x41\x54\x48\x83\xEC\x00\x48\x85\xF6"
        // TODO - get for windows
        : @"";

    public static MemoryFunctionVoid<
        CPlayer_ObserverServices,
        IntPtr
    > SetNextObserveTargetInputFunc = new(_setNextObserveTarget);

    public void SetNextObserveTarget(CPlayer_ObserverServices services, IntPtr unknownParam)
    {
        SetNextObserveTargetInputFunc.Invoke(services, unknownParam);
    }

    private void _watchSpectatorChanges()
    {
        SpectatorChanged.Hook(SpectatorChangedHook, HookMode.Post);
    }

    private IntPtr? previous;

    private HookResult SpectatorChangedHook(DynamicHook handle)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (matchData == null)
        {
            return HookResult.Continue;
        }

        var observerServices = handle.GetParam<CPlayer_ObserverServices>(0);
        var secondParam = handle.GetParam<IntPtr>(1);
        _logger.LogInformation($"2nd param {secondParam}");

        ObserverMode_t desiredMode = ObserverMode_t.OBS_MODE_IN_EYE;

        CBasePlayerController? spectator = observerServices.Pawn.Value.Controller.Value;

        if (spectator == null)
        {
            return HookResult.Continue;
        }

        // _logger.LogInformation($"THIS WILL CRASH {GetNextObserverTarget(observerServices, 0)}");
        if (desiredMode != (ObserverMode_t)observerServices.ObserverMode)
        {
            // _logger.LogWarning($"sepectator tried to view in non eye mode coming from {observerServices.ObserverLastMode}");
            return HookResult.Changed;
        }

        var player = CounterStrikeSharp.API.Utilities.GetPlayerFromSteamId(spectator.SteamID);

        var target = observerServices.ObserverTarget.Value;

        if (target == null || target.IsValid == false)
        {
            return HookResult.Continue;
        }

        int viewingTeamNum = target?.TeamNum ?? -1;

        if (player == null || viewingTeamNum == -1)
        {
            return HookResult.Continue;
        }

        CsTeam viewingTeam = TeamUtility.TeamNumToCSTeam(viewingTeamNum);

        _logger.LogInformation(
            $"Specator ID: {spectator.SteamID} is viewing {viewingTeam} @ {observerServices.ObserverMode}"
        );

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

    
        if (previous != null)
        {
            _logger.LogInformation($"OK LETS GO TO PREV {previous.Value}");
            SetNextObserveTarget(observerServices, previous.Value);
            return HookResult.Changed;
        }

        previous = observerServices.ObserverTarget?.Value?.Handle;

        if (viewingTeam != expectedTeam)
        {
            // return HookResult.Stop;
            return HookResult.Continue;
        }

        return HookResult.Changed;
    }
}
