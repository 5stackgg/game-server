using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_surrender", "Initiates a surrender vote for the player's team")]
    public void Surrender(CCSPlayerController? player, CommandInfo? command)
    {
        if (player == null)
        {
            return;
        }

        _surrenderSystem.SetupSurrender(player.Team, player);
    }
}
