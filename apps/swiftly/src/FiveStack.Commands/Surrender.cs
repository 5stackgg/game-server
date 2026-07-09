using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [Command("surrender", registerRaw: true, permission: "")]
    public void Surrender(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null)
        {
            return;
        }

        _surrenderSystem.SetupSurrender(player.Controller.Team, player);
    }
}
