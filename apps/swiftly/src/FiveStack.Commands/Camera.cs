using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Translation;

namespace FiveStack;

public partial class FiveStackPlugin
{
    // Server-only, and deliberately unprefixed like get_match / force_ready:
    // nothing but the API ever calls it, so it does not need the css_/sw_
    // treatment that player-facing commands do.
    [Command("camera_state", registerRaw: true, permission: "")]
    public void OnCameraState(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            return;
        }

        _cameraSystem.UpdateState(string.Join(",", context.Args));
    }

    [Command("cam", registerRaw: false, permission: "")]
    public void OnCameraStatus(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        ILocalizer localizer = _gameServer.LocalizerFor(player);

        if (!_cameraSystem.IsRequired())
        {
            _gameServer.Message(MessageType.Chat, localizer["camera.not_required"], player);
            return;
        }

        _gameServer.Message(
            MessageType.Chat,
            _cameraSystem.IsPlayerBlocked(player)
                ? localizer["camera.yours_down"]
                : localizer["camera.yours_ok"],
            player
        );
    }
}
