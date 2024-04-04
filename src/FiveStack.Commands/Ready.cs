using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_r", "Toggles the player as ready")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnReady(CCSPlayerController? player, CommandInfo? command)
    {
        MatchManager? match = CurrentMatch();

        if (player == null || match == null || !match.IsWarmup())
        {
            return;
        }

        // TODO - somethigns missing here
    }

    [ConsoleCommand("force_ready", "Forces the match to start")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnForceStart(CCSPlayerController? player, CommandInfo? command)
    {
        MatchManager? match = CurrentMatch();

        if (match == null || !match.IsKnife())
        {
            return;
        }

        _gameServer.Message(HudDestination.Center, $"Game has been forced to start.", player);

        match.UpdateMapStatus(eMapStatus.Knife);
    }
}
