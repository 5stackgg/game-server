using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Utilities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_captain", "Shows / Claims Captain Spots")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnCaptain(CCSPlayerController? player, CommandInfo? command)
    {
        MatchManager? match = CurrentMatch();

        if (player == null || match == null || match.IsWarmup())
        {
            return;
        }

        CsTeam team = TeamUtility.TeamStringToCsTeam(player.TeamNum.ToString());

        if (team == CsTeam.None || team == CsTeam.Spectator)
        {
            return;
        }

        if (match.captainSystem.GetTeamCaptain(team) == null)
        {
            match.captainSystem.ClaimCaptain(team, player);
        }

        match.captainSystem.ShowCaptains();
    }

    [ConsoleCommand("css_release-captain", "Release Captain Spot")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnReleaseCaptain(CCSPlayerController? player, CommandInfo? command)
    {
        MatchManager? match = CurrentMatch();

        if (player == null || match == null || match.IsWarmup())
        {
            return;
        }

        CsTeam team = TeamUtility.TeamStringToCsTeam(player.TeamNum.ToString());

        if (team == CsTeam.None || team == CsTeam.Spectator)
        {
            return;
        }

        match.captainSystem.RemoveTeamCaptain(player, team);

        match.captainSystem.ShowCaptains();
    }
}
