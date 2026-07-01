using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnGameEnd(EventCsWinPanelMatch @event, GameEventInfo info)
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
            // Belt-and-suspenders: OnRoundOfficiallyEnded normally fires for the final round
            // and captures the snapshot. If for any reason it didn't (engine quirk, surrender
            // racing the win panel), capture now from live engine state so the final row gets
            // published below.
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

        if (matchData.options.use_playcast)
        {
            TimerUtility.AddTimer(
                matchData.options.tv_delay,
                () =>
                {
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

                        // Only finish the map once the demo is confirmed uploaded, so
                        // the server stays reserved until it's safe. If the budget
                        // runs out, give up loudly and finish anyway (retried on next
                        // startup) so the match never gets stuck.
                        int uploadTimeLimit =
                            _environmentService.GetDemoUploadTimeLimitSeconds();
                        using var uploadCts = new System.Threading.CancellationTokenSource(
                            TimeSpan.FromSeconds(uploadTimeLimit)
                        );

                        bool uploaded = false;
                        int attempt = 0;
                        while (!uploadCts.IsCancellationRequested)
                        {
                            attempt++;
                            try
                            {
                                uploaded = await _gameDemos.UploadDemos(uploadCts.Token);
                                if (uploaded)
                                {
                                    _logger.LogInformation(
                                        "Demo upload finished (match={MatchId} attempts={Attempts})",
                                        expectedMatchId,
                                        attempt
                                    );
                                    break;
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(
                                    ex,
                                    "UploadDemos failed (match={MatchId} attempt={Attempt})",
                                    expectedMatchId,
                                    attempt
                                );
                            }

                            try
                            {
                                await Task.Delay(TimeSpan.FromSeconds(10), uploadCts.Token);
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }
                        }

                        if (!uploaded)
                        {
                            _logger.LogCritical(
                                "Demo upload did NOT complete within {Limit}s ({Attempts} attempt(s)) — this server may be too slow to upload demos. Finishing the map without a confirmed upload (match={MatchId}); the demo remains on disk and will be retried on next startup.",
                                uploadTimeLimit,
                                attempt,
                                expectedMatchId
                            );
                        }

                        Server.NextFrame(() =>
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
                                "Finishing map and switching (match={MatchId} demoUploaded={Uploaded})",
                                expectedMatchId,
                                uploaded
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
