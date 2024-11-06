using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Utilities;

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

        _gameServer.Message(
            HudDestination.Notify,
            $"{player.PlayerName} has asked to surrender the match, type in {CommandUtility.PublicChatTrigger}y or {CommandUtility.PublicChatTrigger}n to vote"
        );

        _surrenderSystem.SetupSurrender(player.Team);
    }
}
