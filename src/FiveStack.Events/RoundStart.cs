using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        MatchManager? matchManager = _matchService.GetCurrentMatch();
        if (matchManager == null)
        {
            _logger.LogInformation("OnRoundStart: no current match - skipping");
            return HookResult.Continue;
        }

        int totalRoundsPlayed = _gameServer.GetTotalRoundsPlayed();
        bool isInProgress = matchManager.IsInProgress();
        bool isKnife = matchManager.IsKnife();
        bool isWarmup = matchManager.IsWarmup();

        _logger.LogInformation($"OnRoundStart totalRoundsPlayed={totalRoundsPlayed} status={matchManager.CurrentMapStatus} previous={matchManager.PreviousMapStatus} isInProgress={isInProgress} isWarmup={isWarmup} isKnife={isKnife}");

        if (!isInProgress)
        {
            return HookResult.Continue;
        }

        if (_gameBackupRounds.IsResettingRound())
        {
            _logger.LogInformation("OnRoundStart skipping publish: restoring round");
            return HookResult.Continue;
        }

        PublishPendingRound(SendBackupRound: true);

        int currentPlayers = MatchUtility.Players().Count;

        int expectedPlayers = _matchService.GetCurrentMatch()?.GetExpectedPlayerCount() ?? 10;

        if (currentPlayers < expectedPlayers)
        {
            matchManager.PauseMatch("Waiting for players to reconnect");
        }

        return HookResult.Continue;
    }
}
