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

        match.gameEnded = true;

        PublishRoundInformation();

        MatchData? matchData = match.GetMatchData();
        MatchMap? currentMap = match.GetCurrentMap();
        if (matchData == null || currentMap == null)
        {
            return HookResult.Continue;
        }

        if (matchData.options.use_playcast)
        {
            TimerUtility.AddTimer(
                matchData.options.tv_delay,
                () =>
                {
                    HandleEndOfMap();
                }
            );

            return HookResult.Continue;
        }

        HandleEndOfMap();

        return HookResult.Continue;
    }

    private void HandleEndOfMap()
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

        if (_environmentService.isOnGameServerNode())
        {
            _logger.LogInformation(
                "Game Server is on a game server node, skipping uploading demos"
            );
            match.delayChangeMap(usePlaycast ? 5 : matchData.options.tv_delay);

            if (_environmentService.IsOfflineMode())
            {
                HandleOfflineMapProgression(match, matchData, currentMap);
            }

            if (match.isSurrendered())
            {
                SendSurrender();
            }
            else
            {
                match.UpdateMapStatus(eMapStatus.Finished, _matchEvents.GetWinningLineupId());
            }

            return;
        }

        _logger.LogInformation("delaying uploading demos for 15 seconds");

        Guid? winningLineupId = _matchEvents.GetWinningLineupId();
        Guid expectedMatchId = matchData.id;
        int tvDelay = matchData.options.tv_delay;

        match.UpdateMapStatus(eMapStatus.UploadingDemo, winningLineupId);

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

                try
                {
                    await _gameDemos.UploadDemos();
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "UploadDemos failed after map end for match {MatchId}",
                        expectedMatchId
                    );
                }

                Server.NextFrame(() =>
                {
                    if (_matchService.GetCurrentMatch()?.GetMatchData()?.id != expectedMatchId)
                    {
                        _logger.LogWarning(
                            "Skipping end-of-map transition: current match is not {MatchId} after demo upload",
                            expectedMatchId
                        );
                        return;
                    }

                    MatchManager current = _matchService.GetCurrentMatch()!;

                    if (current.isSurrendered())
                    {
                        SendSurrender();
                    }
                    else
                    {
                        current.UpdateMapStatus(
                            eMapStatus.Finished,
                            _matchEvents.GetWinningLineupId()
                        );
                    }

                    int tailDelaySeconds = usePlaycast
                        ? 5
                        : Math.Max(5, tvDelay - 15);

                    current.delayChangeMap(tailDelaySeconds);
                });
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
