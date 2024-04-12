using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
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
        : @"\x48\x89\x5C\x24\x2A\x48\x89\x6C\x24\x2A\x48\x89\x74\x24\x2A\x57\x48\x83\xEC\x2A\x83\xBA\x2A\x2A\x2A\x2A\x2A\x48\x8D\x2D\x2A\x2A\x2A\x2A\x48\x8B\xF2\x48\x8B\xF9";

    public MemoryFunctionWithReturn<CPlayer_ObserverServices, IntPtr, bool> SpectatorChanged =
        new(_specTatorChanged);

    private void _watchSpectatorChanges()
    {
        SpectatorChanged.Hook(OnChangeSpecator, HookMode.Post);
        SpectatorChanged.Hook(OnChangedSpectatorMode, HookMode.Pre);
    }

    private HookResult OnChangedSpectatorMode(DynamicHook handle)
    {
        ObserverMode_t desiredMode = ObserverMode_t.OBS_MODE_IN_EYE;

        var observerServices = handle.GetParam<CPlayer_ObserverServices>(0);
        var spectateCommand = _getSpecatorCommand(handle.GetParam<IntPtr>(1));

        // force them to stay in eye mode
        if (
            spectateCommand == "spec_mode"
            && desiredMode == (ObserverMode_t)observerServices.ObserverMode
        )
        {
            handle.SetReturn(false);
            return HookResult.Stop;
        }

        // TODO - what happens if they aren't in this mode (on connect?)

        handle.SetReturn(true);
        return HookResult.Continue;
    }

    private HookResult OnChangeSpecator(DynamicHook handle)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (matchData == null)
        {
            handle.SetReturn(true);
            return HookResult.Continue;
        }

        var observerServices = handle.GetParam<CPlayer_ObserverServices>(0);
        var spectateCommand = _getSpecatorCommand(handle.GetParam<IntPtr>(1));

        CBasePlayerController? spectator = observerServices.Pawn.Value.Controller.Value;

        if (spectator == null || spectator.Pawn.Value?.ObserverServices == null)
        {
            _logger.LogWarning("spec is null");
            handle.SetReturn(true);
            return HookResult.Continue;
        }

        CBaseEntity? target = observerServices.ObserverTarget.Value;

        if (target == null)
        {
            _logger.LogWarning("target is null");
            handle.SetReturn(true);
            return HookResult.Continue;
        }

        var targetPlayer = new CBasePlayerPawn(target.Handle);

        int viewingTeamNum = target?.TeamNum ?? -1;

        if (viewingTeamNum == -1)
        {
            _logger.LogWarning("viewing team is none");
            handle.SetReturn(true);
            return HookResult.Continue;
        }

        CsTeam viewingTeam = TeamUtility.TeamNumToCSTeam(viewingTeamNum);

        _logger.LogInformation(
            $"{spectator.PlayerName} is viewing [{viewingTeam}] {targetPlayer.Controller.Value?.PlayerName} @ {(ObserverMode_t)observerServices.ObserverMode}"
        );

        var player = CounterStrikeSharp.API.Utilities.GetPlayerFromSteamId(spectator.SteamID);

        if (player == null)
        {
            _logger.LogWarning("player is null");
            handle.SetReturn(true);
            return HookResult.Continue;
        }

        Guid? lineup_id = MatchUtility.GetPlayerLineup(matchData, player);

        if (lineup_id == null)
        {
            _logger.LogWarning("lineup_id is null");
            handle.SetReturn(true);
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
                break;
            }
        }

        if (viewingTeam != expectedTeam)
        {
            var forceViewingPlayer = MatchUtility
                .Players()
                .Find(player =>
                {
                    return player.Team == expectedTeam;
                });

            if (forceViewingPlayer == null)
            {
                // TODO - what happens if no one is on the team yet?
                _logger.LogWarning("no available players to spectate");
                return HookResult.Continue;
            }

            observerServices.ObserverTarget.Raw = forceViewingPlayer.EntityHandle.Raw;
            handle.SetReturn(true);
            return HookResult.Changed;
        }

        handle.SetReturn(true);
        return HookResult.Continue;
    }

    private string _getSpecatorCommand(IntPtr command)
    {
        var sizePtr = IntPtr.Add(command, 0x438);
        var size = Marshal.ReadInt32(sizePtr);

        var commandPtrPtr = IntPtr.Add(command, 0x440);
        var commandPtr = Marshal.ReadIntPtr(commandPtrPtr);

        var commandBytes = new byte[size];
        Marshal.Copy(commandPtr, commandBytes, 0, size);

        return CounterStrikeSharp.API.Utilities.ReadStringUtf8(commandPtr);
    }
}
