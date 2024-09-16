using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

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

        if (!_gameBackupRounds.IsResettingRound())
        {
            _gameBackupRounds.CastVote(player, command.GetCommandString == "css_y");
        }

        if (_surrender.IsSurrendering())
        {
            _surrender.CastVote(player, command.GetCommandString == "css_y");
        }
    }
}
