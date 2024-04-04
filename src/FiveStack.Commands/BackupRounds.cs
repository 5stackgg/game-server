using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using FiveStack.Entities;
using FiveStack.Utilities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("restore_round", "Restores to a previous round")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void ResetRound(CCSPlayerController? player, CommandInfo command)
    {
        string round = command.ArgByIndex(1);
        _gameBackupRounds.LoadRound(round);
    }

    [ConsoleCommand("css_reset", "Restores to a previous round")]
    public void RestoreRound(CCSPlayerController? player, CommandInfo command)
    {
        // TODO - round can be null, reset to -1 round
        string round = command.ArgByIndex(1);
        FiveStackMatch? matchData = CurrentMatch()?.GetMatchData();

        if (matchData == null || round == null)
        {
            return;
        }

        bool isResttingRound = _gameBackupRounds.IsResttingRound();
        if (player != null && isResttingRound)
        {
            if (
                player.UserId != null
                && MatchUtility.GetMemberFromLineup(matchData, player)?.captain == true
            )
            {
                string vote = command.ArgByIndex(1);

                // TODO - different command to progress failure?
                // mabye just a .y / .n
                if (vote != null)
                {
                    _gameBackupRounds.VoteFailed();
                    return;
                }

                _gameBackupRounds.Vote(player);
            }

            return;
        }

        _gameBackupRounds.RestoreBackupRound(round, player);
    }

    [ConsoleCommand("upload_backup_round", "upload backup round")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public async void upload_backup_round(CCSPlayerController? player, CommandInfo command)
    {
        string round = command.ArgByIndex(1);

        if (round == null)
        {
            return;
        }

        await _gameBackupRounds.UploadBackupRound(round);
    }
}
