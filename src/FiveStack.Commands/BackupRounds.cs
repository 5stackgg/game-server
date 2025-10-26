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
        string _round = command.ArgByIndex(1);

        if (string.IsNullOrWhiteSpace(_round))
        {
            _logger.LogWarning("Invalid round number provided");
            return;
        }

        if (!int.TryParse(_round, out int round))
        {
            _logger.LogWarning($"Failed to parse round number: {_round}");
            return;
        }

        _gameBackupRounds.SendRestoreRoundToBackend(round);
    }

    [ConsoleCommand(
        "api_restore_round",
        "Should only be called by the API, this is so we know the api regonized the restore"
    )]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnApiResetRound(CCSPlayerController? player, CommandInfo command)
    {
        string _round = command.ArgByIndex(1);
        _logger.LogInformation($"API Restoring Round {_round}");

        if (string.IsNullOrWhiteSpace(_round))
        {
            _logger.LogWarning("Invalid round number provided");
            return;
        }

        if (!int.TryParse(_round, out int round))
        {
            _logger.LogWarning($"Failed to parse round number: {_round}");
            return;
        }

        _gameBackupRounds.RestoreRound(round);
    }

    [ConsoleCommand("css_reset", "Restores to a previous round")]
    public void OnRestoreRound(CCSPlayerController? player, CommandInfo command)
    {
        string _round = command.ArgByIndex(1);

        if (string.IsNullOrWhiteSpace(_round) || _gameBackupRounds.IsResettingRound())
        {
            if (_gameBackupRounds.IsResettingRound())
            {
                _logger.LogWarning("Already restoring round, skipping");
            }
            else
            {
                _logger.LogWarning("Invalid round number provided");
            }
            return;
        }

        if (!int.TryParse(_round, out int round))
        {
            _logger.LogWarning($"Failed to parse round number: {_round}");
            return;
        }

        _gameBackupRounds.RequestRestoreBackupRound(round, player);
    }
}
