using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_n", "Casts vote for Resetting a Round")]
    [ConsoleCommand("css_y", "Casts vote for Resetting a Round")]
    public void OnResetAnswer(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null)
        {
            return;
        }

        if (_gameBackupRounds.IsResettingRound())
        {
            _gameBackupRounds.restoreRoundVote?.CastVote(
                player,
                command.GetCommandString == "css_y"
            );
        }

        if (_surrenderSystem.IsSurrendering())
        {
            _surrenderSystem.surrenderingVote?.CastVote(
                player,
                command.GetCommandString == "css_y"
            );
        }

        if (_timeoutSystem.resumeVote != null)
        {
            _timeoutSystem.resumeVote?.CastVote(player, command.GetCommandString == "css_y");
        }
    }
}
