using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace FiveStack;

public partial class FiveStackPlugin
{
    // Server-only, and deliberately unprefixed like get_match / force_ready:
    // nothing but the API ever calls it, so it does not need the css_/sw_
    // treatment that player-facing commands do.
    [ConsoleCommand("camera_state", "Reports which players have no working camera")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnCameraState(CCSPlayerController? player, CommandInfo? command)
    {
        if (command == null)
        {
            return;
        }

        _cameraSystem.UpdateState(command.ArgByIndex(1) ?? "");
    }

    [ConsoleCommand("css_cam", "Shows your camera status")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnCameraStatus(CCSPlayerController? player, CommandInfo? command)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        if (!_cameraSystem.IsRequired())
        {
            _gameServer.Message(HudDestination.Chat, _localizer["camera.not_required"], player);
            return;
        }

        _gameServer.Message(
            HudDestination.Chat,
            _cameraSystem.IsPlayerBlocked(player)
                ? _localizer["camera.yours_down"]
                : _localizer["camera.yours_ok"],
            player
        );
    }
}
