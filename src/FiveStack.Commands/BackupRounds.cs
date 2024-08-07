using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
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

        _gameBackupRounds.RestoreRound(round);
    }

    [ConsoleCommand(
        "api_restore_round",
        "Should only be called by the API, this is so we know the api regonized the restore"
    )]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnApiResetRound(CCSPlayerController? player, CommandInfo command)
    {
        string round = command.ArgByIndex(1);

        if (round == null)
        {
            return;
        }

        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match?.current_match_map_id == null)
        {
            _logger.LogWarning("unable to load road because we dont have the current map");
            return;
        }

        string backupRoundFile =
            $"{MatchUtility.GetSafeMatchPrefix(match)}_round{round.PadLeft(2, '0')}.txt";

        if (!_gameBackupRounds.HasBackupRound(round))
        {
            return;
        }

        _gameServer.SendCommands(new[] { $"mp_backup_restore_load_file {backupRoundFile}" });

        _gameBackupRounds.ResetRestoreBackupRound();

        _gameServer.Message(
            HudDestination.Alert,
            $" {ChatColors.Red}Round {round} has been restored (.resume to continue)"
        );
    }

    [ConsoleCommand("css_reset", "Restores to a previous round")]
    public void OnRestoreRound(CCSPlayerController? player, CommandInfo command)
    {
        string round = command.ArgByIndex(1);

        // TODO - round can be null, reset to -1 round
        if (round == null || _gameBackupRounds.IsResettingRound())
        {
            return;
        }

        _gameBackupRounds.RestoreBackupRound(round, player);
    }

    [ConsoleCommand("css_no", "Casts vote for Resetting a Round")]
    [ConsoleCommand("css_yes", "Casts vote for Resetting a Round")]
    public void OnResetAnswer(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !_gameBackupRounds.IsResettingRound())
        {
            return;
        }

        _gameBackupRounds.CastVote(
            player,
            command.GetCommandString == "css_yes" || command.GetCommandString == "css_y"
        );
    }

    [ConsoleCommand("download_backup_rounds", "downloads the backup rounds from the api")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public async void OnDownloadBackupRounds(CCSPlayerController? player, CommandInfo command)
    {
        await _gameBackupRounds.DownloadBackupRounds();
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
