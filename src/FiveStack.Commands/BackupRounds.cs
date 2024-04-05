using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("restore_round", "Restores to a previous round")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnResetRound(CCSPlayerController? player, CommandInfo command)
    {
        string round = command.ArgByIndex(1);

        if (round == null)
        {
            return;
        }

        _gameBackupRounds.LoadRound(round);
    }

    [ConsoleCommand("css_reset", "Restores to a previous round")]
    public void OnRestoreRound(CCSPlayerController? player, CommandInfo command)
    {
        string round = command.ArgByIndex(1);

        // TODO - round can be null, reset to -1 round
        if (round == null)
        {
            return;
        }

        bool isResttingRound = _gameBackupRounds.IsResttingRound();
        if (player != null && isResttingRound)
        {
            _gameBackupRounds.CastVote(player, command.ArgByIndex(1));
            return;
        }

        _gameBackupRounds.RestoreBackupRound(round, player);
    }

    [ConsoleCommand("download_backup_rounds", "downloads the backup rounds from the api")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnDownloadBackupRounds(CCSPlayerController? player, CommandInfo command)
    {
        _gameBackupRounds.DownloadBackupRounds();
    }

    [ConsoleCommand("upload_backup_round", "upload backup round")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public async void OnUploadBackupRound(CCSPlayerController? player, CommandInfo command)
    {
        string round = command.ArgByIndex(1);

        if (round == null)
        {
            return;
        }

        await _gameBackupRounds.UploadBackupRound(round);
    }
}
