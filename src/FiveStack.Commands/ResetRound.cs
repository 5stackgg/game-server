using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace FiveStack;

public partial class FiveStackPlugin
{

    [ConsoleCommand("restore_round", "Restores to a previous round")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void ResetRound(CCSPlayerController? player, CommandInfo command){
       string round = command.ArgByIndex(1);
       string backupRoundFile = $"{GetSafeMatchPrefix()}_round{round.PadLeft(2, '0')}.txt";
       
        SendCommands(new[] { $"mp_backup_restore_load_file {backupRoundFile}" });

        Message(
            HudDestination.Alert,
            $" {ChatColors.Red}Round {round} has been restored (.resume to continue)"
        );
        ResetRestoreBackupRound();
    }

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
