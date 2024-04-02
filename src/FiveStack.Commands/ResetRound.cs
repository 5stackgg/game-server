using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_reset", "Restores to a previous round")]
    public void RestoreRound(CCSPlayerController? player, CommandInfo command)
    {
        if (_matchData == null)
        {
            return;
        }

        if (player != null && _resetRound != null)
        {
            if (player.UserId != null && GetMemberFromLineup(player)?.captain == true)
            {
                string vote = command.ArgByIndex(1);

                if (vote != null)
                {
                    ResetRestoreBackupRound();

                    Message(
                        HudDestination.Alert,
                        $" {ChatColors.Red}Captain denied request to reset round to {_resetRound}"
                    );

                    return;
                }

                _restoreRoundVote[player.UserId.Value] = true;
            }

            if (_restoreRoundVote.Count(pair => pair.Value) == 2)
            {
                LoadRound(_resetRound);
            }

            return;
        }

        string round = command.ArgByIndex(1);

        if (RestoreBackupRound(round, player != null) == false)
        {
            command.ReplyToCommand($"Unable to restore round, missing file");
            return;
        }

        if (player != null && player.UserId != null && GetMemberFromLineup(player)?.captain == true)
        {
            _restoreRoundVote[player.UserId.Value] = true;
        }
    }
}
