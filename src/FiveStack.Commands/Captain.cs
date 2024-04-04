using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_captain", "Shows / Claims Captain Spots")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnCaptain(CCSPlayerController? player, CommandInfo? command)
    {
        if (player == null || (_matchService.IsWarmup()))
        {
            return;
        }

        CsTeam team = TeamUtility.TeamStringToCsTeam(player.TeamNum.ToString());

        if (team == CsTeam.None || team == CsTeam.Spectator)
        {
            return;
        }

        FiveStackMatch match = _matchService.GetCurrentMatchData()!;

        // autoclaim captain
        if (_matchService?.captainSystem?.TeamHasCaptain(team) == false)
        {
            _matchService.captainSystem?.ClaimCaptain(match, team, player);
        }

        _matchService?.captainSystem?.ShowCaptains();
    }

    [ConsoleCommand("css_release-captain", "Release Captain Spot")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnReleaseCaptain(CCSPlayerController? player, CommandInfo? command)
    {
        if (player == null || (_matchService.IsWarmup()))
        {
            return;
        }

        CsTeam team = TeamUtility.TeamStringToCsTeam(player.TeamNum.ToString());

        if (team == CsTeam.None || team == CsTeam.Spectator)
        {
            return;
        }

        FiveStackMatch match = _matchService.GetCurrentMatchData()!;

        _matchService?.captainSystem?.RemoveTeamCaptain(match, player, team);

        _matchService?.captainSystem?.ShowCaptains();
    }
}
