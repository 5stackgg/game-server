using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [Command("restore_round", registerRaw: true, permission: "")]
    public void OnResetRound(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            return;
        }

        string _round = context.Args.Length > 0 ? context.Args[0] : "";

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

    [Command("api_restore_round", registerRaw: true, permission: "")]
    public void OnApiResetRound(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            return;
        }

        string _round = context.Args.Length > 0 ? context.Args[0] : "";
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

    [Command("reset", registerRaw: false, permission: "")]
    public void OnRestoreRound(ICommandContext context)
    {
        string _round = context.Args.Length > 0 ? context.Args[0] : "";

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

        _gameBackupRounds.RequestRestoreBackupRound(round, context.Sender);
    }
}
