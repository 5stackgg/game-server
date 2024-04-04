using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_coaches", "Shows Coaches ")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnCoaches(CCSPlayerController? player, CommandInfo? command)
    {
        ShowCoaches();
    }

    [ConsoleCommand("css_coach", "Shows / Claims Coach Spots ")]
    [CommandHelper(minArgs: 1, usage: "[ct,t]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnCoach(CCSPlayerController? player, CommandInfo? command)
    {
        if (
            player == null
            || _currentMap == null
            || (MapStatusStringToEnum(_currentMap.status) != eMapStatus.Warmup)
        )
        {
            return;
        }

        CsTeam team = TeamNumToCSTeam(player.TeamNum);
        string? _team = command?.ArgByIndex(0);

        if (_team != null)
        {
            if (_team == "t")
            {
                team = CsTeam.Terrorist;
            }
            else if (_team == "ct")
            {
                team = CsTeam.CounterTerrorist;
            }
        }

        _coaches[team] = player;

        ShowCoaches();
    }

    [ConsoleCommand("css_release-coach", "Release Coach Spot")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnReleaseCoach(CCSPlayerController? player, CommandInfo? command)
    {
        if (
            player == null
            || _currentMap == null
            || (MapStatusStringToEnum(_currentMap.status) != eMapStatus.Warmup)
        )
        {
            return;
        }

        CsTeam team = TeamNumToCSTeam(player.TeamNum);

        if (team == CsTeam.None || team == CsTeam.Spectator)
        {
            return;
        }

        _coaches[team] = null;

        ShowCoaches();
    }

    public void ShowCoaches()
    {
        CsTeam[] teams = { CsTeam.CounterTerrorist, CsTeam.Terrorist };

        foreach (CsTeam team in teams)
        {
            if (_coaches[team] == null)
            {
                Message(
                    HudDestination.Notify,
                    $"[{TeamNumToString((int)team)} Coach] {ChatColors.Green}/coach t or /coach ct"
                );
                continue;
            }

            Message(
                HudDestination.Notify,
                $"[{TeamNumToString((int)team)} Coach] {(team == CsTeam.Terrorist ? ChatColors.Gold : ChatColors.Blue)}{_coaches[team]?.PlayerName}"
            );
        }
    }
}
