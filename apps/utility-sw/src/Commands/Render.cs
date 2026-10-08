using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;

namespace UtilityPractice;

// The render pod's verbs. It sends them as chat with the silent prefix
// ("say /render_stage <id>") because a client cannot invoke a Swiftly console
// command directly. Only a server booked for renders listens at all.
public partial class UtilityPracticePlugin
{
    [Command("render_stage", registerRaw: false, permission: "")]
    public void OnRenderStage(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (!_config.RenderMode || player == null || !player.IsValid)
        {
            return;
        }

        string lineupId = string.Join(" ", context.Args).Trim().Trim('"');

        if (!Guid.TryParse(lineupId, out _))
        {
            player.SendMessage(
                MessageType.Console,
                FiveStack.Utilities.RenderDirectorUtility.Line("error", ("reason", "bad_lineup_id")) + "\n"
            );
            return;
        }

        _director.Stage(player, lineupId);
    }

    [Command("render_go", registerRaw: false, permission: "")]
    public void OnRenderGo(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (!_config.RenderMode || player == null || !player.IsValid)
        {
            return;
        }

        _director.Go(player);
    }

    [Command("render_reset", registerRaw: false, permission: "")]
    public void OnRenderReset(ICommandContext context)
    {
        if (!_config.RenderMode)
        {
            return;
        }

        _director.Reset(null);
    }
}
