using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;

namespace FiveStack;

public partial class FiveStackPlugin
{
    private readonly Dictionary<ulong, int> _cashEarnedAtRoundStart = new();

    [GameEventHandler(HookMode.Post)]
    public HookResult OnRoundStart(EventRoundStart @event)
    {
        _victimHealth.Clear();
        RoundMoney.Baseline(_cashEarnedAtRoundStart, TeamUtility.PlayingMoney());

        MatchManager? matchManager = _matchService.GetCurrentMatch();
        if (matchManager == null)
        {
            _logger.LogInformation("OnRoundStart: no current match - skipping");
            return HookResult.Continue;
        }

        _rankSystem.Refresh();

        int totalRoundsPlayed = _gameServer.GetTotalRoundsPlayed();
        bool isInPlay = matchManager.IsInPlay();
        bool isKnife = matchManager.IsKnife();
        bool isWarmup = matchManager.IsWarmup();

        _logger.LogInformation(
            $"OnRoundStart totalRoundsPlayed={totalRoundsPlayed} isInPlay={isInPlay} isWarmup={isWarmup} isKnife={isKnife}"
        );

        if (
            _environmentService.IsOfflineMode()
            && matchManager.isSurrendered()
            && !matchManager.gameEnded
        )
        {
            _logger.LogWarning("The surrender did not end the game, ending the map from here");
            _gameServer.SendCommands(["mp_pause_match"]);
            ConcludeMap();
            return HookResult.Continue;
        }

        if (!isInPlay)
        {
            return HookResult.Continue;
        }

        if (_gameBackupRounds.HoldsRoundResults())
        {
            // A restart can unpause the round the hold paused.
            if (_gameBackupRounds.IsHoldingForRestore())
            {
                _gameServer.SendCommands(["mp_pause_match"]);
            }

            _logger.LogInformation("OnRoundStart skipping publish: restoring round");
            return HookResult.Continue;
        }

        PublishPendingRound(SendBackupRound: true);

        _matchService.SaveOfflineProgress();

        _logger.LogInformation($"OnRoundStart {matchManager.TeamSwitchState()}");

        matchManager.ReconcileMemberTeams();

        int expectedPlayers = _matchService.GetCurrentMatch()?.GetExpectedPlayerCount() ?? 10;
        MatchData? roundMatchData = matchManager.GetMatchData();

        if (
            roundMatchData != null
            && ReconnectPauseUtility.ShouldPause(
                roundMatchData,
                MatchUtility.Players().Select(player => (player.SteamID.ToString(), player.Name)),
                expectedPlayers
            )
            && matchManager.timeoutSystem.HoldForRoster(
                MatchUtility.Players().Select(player => (player.SteamID.ToString(), player.Name))
            )
        )
        {
            matchManager.PauseMatch(
                ePauseReason.Roster,
                localizer => localizer["match.waiting_reconnect"],
                stillRequired: () =>
                    ReconnectPauseUtility.ShouldPause(
                        matchManager.GetMatchData() ?? roundMatchData,
                        MatchUtility.Players().Select(player => (player.SteamID.ToString(), player.Name)),
                        matchManager.GetExpectedPlayerCount()
                    ) && matchManager.timeoutSystem.HoldsForRoster()
            );
        }

        _cameraSystem.PauseIfOwed();

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnRoundFreezeEnd(EventRoundFreezeEnd @event)
    {
        MatchManager? matchManager = _matchService.GetCurrentMatch();

        if (
            matchManager != null
            && matchManager.IsKnife()
            && MatchUtility.Rules()?.WarmupPeriod != true
        )
        {
            matchManager.knifeSystem.MarkRoundLive();
        }

        return HookResult.Continue;
    }
}
