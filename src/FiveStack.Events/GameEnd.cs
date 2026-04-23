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

        if(IsPlaycasting())
        {
            TimerUtility.AddTimer(
            15.0f,
            async () =>
            {
                HandleEndOfMap();
            }
        );

          return HookResult.Continue;    
        }

        HandleEndOfMap();

        return HookResult.Continue;
    }

    private void HandleEndOfMap() {
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

        if (_environmentService.isOnGameServerNode())
        {
            _logger.LogInformation(
                "Game Server is on a game server node, skipping uploading demos"
            );
            match.delayChangeMap(IsPlaycasting() ? 5 : matchData.options.tv_delay);

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

        match.UpdateMapStatus(eMapStatus.UploadingDemo, _matchEvents.GetWinningLineupId());

        TimerUtility.AddTimer(
            15.0f,
            async () =>
            {
                await _gameDemos.UploadDemos();

                Server.NextFrame(() =>
                {
                    if (match.isSurrendered())
                    {
                        SendSurrender();
                    }
                    else
                    {
                        match.UpdateMapStatus(eMapStatus.Finished);
                    }

                    match.delayChangeMap(IsPlaycasting() ? 0 : Math.Max(5, matchData.options.tv_delay - 15));
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

        match.UpdateMapStatus(eMapStatus.Finished);
    }

    private bool IsPlaycasting() {
        return playcastSystem.IsPlaycasting();
    }
}
