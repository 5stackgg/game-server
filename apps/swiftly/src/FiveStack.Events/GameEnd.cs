using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler(HookMode.Post)]
    public HookResult OnGameEnd(EventCsWinPanelMatch @event)
    {
        _logger.LogInformation("Game ended");

        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null)
        {
            return HookResult.Continue;
        }

        {
            MatchData? snapMatchData = match.GetMatchData();
            MatchMap? snapCurrentMap = match.GetCurrentMap();
            _logger.LogInformation(
                $"OnGameEnd entry: match={snapMatchData?.id} current_match_map_id={snapMatchData?.current_match_map_id} active_map_id={match.GetActiveMapId()} currentMap.id={snapCurrentMap?.id} currentMap.lineup_1_side={snapCurrentMap?.lineup_1_side} currentMap.lineup_2_side={snapCurrentMap?.lineup_2_side} lineup_1_id={snapMatchData?.lineup_1_id} lineup_2_id={snapMatchData?.lineup_2_id} mr={snapMatchData?.options?.mr} isSurrendered={match.isSurrendered()} gameEnded={match.gameEnded}"
            );
        }

        match.gameEnded = true;

        {
            MatchData? capData = match.GetMatchData();
            MatchMap? capMap = match.GetCurrentMap();
            if (
                _matchEvents.PendingRoundResult == null
                && capData != null
                && capMap != null
                && !match.IsKnife()
            )
            {
                _logger.LogInformation(
                    "OnGameEnd: no pending round captured yet, capturing now from live engine state"
                );
                CaptureRoundResult(match, capData, capMap);
            }
        }
        PublishPendingRound(SendBackupRound: false);

        MatchData? matchData = match.GetMatchData();
        MatchMap? currentMap = match.GetCurrentMap();
        if (matchData == null || currentMap == null)
        {
            _logger.LogWarning(
                $"OnGameEnd: matchData or currentMap became null after PublishPendingRound (matchData={matchData == null}, currentMap={currentMap == null})"
            );
            return HookResult.Continue;
        }

        Guid? winningLineupId = _matchEvents.GetWinningLineupId();

        _logger.LogInformation(
            "OnGameEnd: dispatching end-of-map (use_playcast={UsePlaycast} tv_delay={TvDelay} onGameNode={OnGameNode} winningLineupId={WinningLineupId})",
            matchData.options.use_playcast,
            matchData.options.tv_delay,
            _environmentService.isOnGameServerNode(),
            winningLineupId
        );

        // Move the map off Live immediately so it reflects WaitingForTV during the
        // tv_delay window (HandleEndOfMap may be deferred by use_playcast). This is
        // deduped in UpdateMapStatus, so HandleEndOfMap re-setting it is a no-op.
        match.UpdateMapStatus(eMapStatus.WaitingForTV, winningLineupId);

        if (matchData.options.use_playcast)
        {
            _logger.LogInformation(
                "OnGameEnd: use_playcast enabled, deferring HandleEndOfMap by {TvDelay}s",
                matchData.options.tv_delay
            );
            match.StartPlaycastWindowHeartbeat(matchData.options.tv_delay);

            TimerUtility.AddTimer(
                matchData.options.tv_delay,
                () =>
                {
                    _logger.LogInformation(
                        "OnGameEnd: playcast tv_delay elapsed, running HandleEndOfMap"
                    );
                    match.StopPlaycastWindowHeartbeat();
                    HandleEndOfMap(winningLineupId);
                }
            );

            return HookResult.Continue;
        }

        HandleEndOfMap(winningLineupId);

        return HookResult.Continue;
    }

    private void HandleEndOfMap(Guid? winningLineupId)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null)
        {
            return;
        }

        MatchData? matchData = match.GetMatchData();
        MatchMap? currentMap = match.GetCurrentMap();
        if (matchData == null || currentMap == null)
        {
            return;
        }

        bool usePlaycast = matchData.options.use_playcast;
        int tvDelay = matchData.options.tv_delay;
        int recordingDelay = usePlaycast ? 5 : tvDelay;
        Guid expectedMatchId = matchData.id;
        bool wasSurrendered = match.isSurrendered();
        bool onGameNode = _environmentService.isOnGameServerNode();

        AnnounceMapConclusion(matchData, currentMap, winningLineupId);

        _logger.LogInformation(
            "End of map: match={MatchId} map={MapId} onGameNode={OnGameNode} usePlaycast={UsePlaycast} tvDelay={TvDelay} recordingDelay={RecordingDelay} surrendered={Surrendered}",
            matchData.id,
            currentMap.id,
            onGameNode,
            usePlaycast,
            tvDelay,
            recordingDelay,
            wasSurrendered
        );

        match.UpdateMapStatus(eMapStatus.WaitingForTV, winningLineupId);

        _logger.LogInformation(
            "WaitingForTV for {RecordingDelay}s then {NextAction}",
            recordingDelay,
            onGameNode ? "Finished (game node, no upload)" : "stop + 15s flush + upload"
        );

        TimerUtility.AddTimer(
            recordingDelay,
            () =>
            {
                if (_matchService.GetCurrentMatch()?.GetMatchData()?.id != expectedMatchId)
                {
                    _logger.LogWarning(
                        "Skipping demo stop: current match is no longer {MatchId} after recording window",
                        expectedMatchId
                    );
                    return;
                }

                _logger.LogInformation("recording window elapsed, stopping demo recording");
                _gameDemos.Stop();

                MatchManager current = _matchService.GetCurrentMatch()!;
                bool isSurrendered = wasSurrendered || current.isSurrendered();

                if (onGameNode)
                {
                    _logger.LogInformation(
                        "Game node: skipping upload, marking Finished (match {MatchId})",
                        expectedMatchId
                    );

                    if (isSurrendered)
                    {
                        SendSurrender();
                    }
                    else
                    {
                        current.UpdateMapStatus(eMapStatus.Finished, winningLineupId);
                    }

                    if (_environmentService.IsOfflineMode())
                    {
                        HandleOfflineMapProgression(current, matchData, currentMap);
                    }
                    else
                    {
                        current.delayChangeMap(5);
                    }
                    return;
                }

                current.UpdateMapStatus(eMapStatus.UploadingDemo, winningLineupId);

                _logger.LogInformation(
                    "Scheduling demo upload in 15s (match={MatchId} map={MapId})",
                    expectedMatchId,
                    currentMap.id
                );

                TimerUtility.AddTimer(
                    15.0f,
                    async () =>
                    {
                        if (_matchService.GetCurrentMatch()?.GetMatchData()?.id != expectedMatchId)
                        {
                            _logger.LogWarning(
                                "Skipping demo upload: current match is no longer {MatchId} after pre-upload delay",
                                expectedMatchId
                            );
                            return;
                        }

                        _logger.LogInformation(
                            "Starting demo upload (match={MatchId} map={MapId})",
                            expectedMatchId,
                            currentMap.id
                        );

                        try
                        {
                            await _gameDemos.UploadDemos();
                            _logger.LogInformation(
                                "Demo upload finished (match={MatchId})",
                                expectedMatchId
                            );
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "UploadDemos failed after map end for match {MatchId}",
                                expectedMatchId
                            );
                        }

                        _core.Scheduler.NextTick(() =>
                        {
                            if (
                                _matchService.GetCurrentMatch()?.GetMatchData()?.id
                                != expectedMatchId
                            )
                            {
                                _logger.LogWarning(
                                    "Skipping end-of-map transition: current match is not {MatchId} after demo upload",
                                    expectedMatchId
                                );
                                return;
                            }

                            MatchManager next = _matchService.GetCurrentMatch()!;
                            bool isSurrenderedNow = wasSurrendered || next.isSurrendered();

                            _logger.LogInformation(
                                "Demo upload done — finishing map and switching (match={MatchId})",
                                expectedMatchId
                            );

                            if (isSurrenderedNow)
                            {
                                SendSurrender();
                            }
                            else
                            {
                                next.UpdateMapStatus(eMapStatus.Finished, winningLineupId);
                            }

                            next.delayChangeMap(5);
                        });
                    }
                );
            }
        );
    }

    // Tells the server what happens next once a map wraps. Without it the round
    // just ends and players are left guessing whether to stay connected.
    //
    // Keyed on whether another map is queued -- the same check the map
    // progression itself uses -- rather than on who won, which the game-server
    // does not track per map.
    //
    // Deliberately no time estimate: how long the demo takes depends on
    // tv_delay and transfer speed, so a number here would be a guess that reads
    // as a promise.
    private void AnnounceMapConclusion(
        MatchData matchData,
        MatchMap currentMap,
        Guid? winningLineupId
    )
    {
        try
        {
            bool hasNextMap = matchData
                .match_maps.Where(m => m.order == currentMap.order + 1)
                .Any();

            // match_maps holds every vetoed map, including ones a decided
            // series will never play -- a 2-0 best of three still has a third
            // slot sitting there. Count the wins instead, including this map's,
            // which is not on matchData yet at this point.
            int mapsToWin = (matchData.options.best_of / 2) + 1;

            int lineup1Wins = matchData.match_maps.Count(m =>
                m.winning_lineup_id == matchData.lineup_1_id
            );
            int lineup2Wins = matchData.match_maps.Count(m =>
                m.winning_lineup_id == matchData.lineup_2_id
            );

            if (winningLineupId == matchData.lineup_1_id)
            {
                lineup1Wins++;
            }
            else if (winningLineupId == matchData.lineup_2_id)
            {
                lineup2Wins++;
            }

            bool seriesDecided = lineup1Wins >= mapsToWin || lineup2Wins >= mapsToWin;

            _gameServer.Message(
                MessageType.Chat,
                hasNextMap && !seriesDecided
                    ? _localizer["match.map_over_next_map"]
                    : _localizer["match.map_over_series_done"]
            );
        }
        catch (Exception error)
        {
            // Never let a chat message interfere with ending the map.
            _logger.LogWarning(error, "failed to announce map conclusion");
        }
    }

    private void HandleOfflineMapProgression(
        MatchManager match,
        MatchData matchData,
        MatchMap currentMap
    )
    {
        _logger.LogInformation(
            $"Handling offline map progression for map {currentMap.map.name} (order: {currentMap.order})"
        );

        match.UpdateMapStatus(eMapStatus.Finished);

        MatchMap? nextMap = matchData
            .match_maps.Where(m => m.order == currentMap.order + 1)
            .FirstOrDefault();

        if (nextMap == null)
        {
            return;
        }

        _logger.LogInformation(
            $"Advancing to next map: {nextMap.map.name} (order: {nextMap.order})"
        );

        matchData.current_match_map_id = nextMap.id;

        nextMap.status = eMapStatus.Warmup.ToString();

        match.ChangeMap(nextMap.map);
    }

    private void SendSurrender()
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null)
        {
            return;
        }

        Guid? winningLineupId = _surrenderSystem.GetWinningLineupId();
        if (winningLineupId != null)
        {
            _matchEvents.PublishGameEvent(
                "surrender",
                new Dictionary<string, object>
                {
                    { "time", DateTime.Now },
                    { "winning_lineup_id", winningLineupId },
                }
            );
        }

        match.UpdateMapStatus(eMapStatus.Finished, winningLineupId);
    }
}
