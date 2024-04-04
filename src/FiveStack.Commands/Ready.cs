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
        if (!_matchService.IsWarmup() || player == null)
        {
            return;
        }
    }

    [ConsoleCommand("force_ready", "Forces the match to start")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnForceStart(CCSPlayerController? player, CommandInfo? command)
    {
        if (!_matchService.IsWarmup())
        {
            return;
        }

        _gameServer.Message(HudDestination.Center, $"Game has been forced to start.", player);

        _matchService.UpdateMapStatus(eMapStatus.Knife);
    }
}
