using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_surrender", "TEMP")]
    public void Surrender(CCSPlayerController? player, CommandInfo? command)
    {
        if (player == null)
        {
            return;
        }

        _gameServer.Message(
            HudDestination.Notify,
            $"{player.PlayerName} has asked to surrender the match, type in .y or .n to vote"
        );

        _surrender.setupSurrender(player.Team);
    }
}
