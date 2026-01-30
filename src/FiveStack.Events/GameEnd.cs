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
        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null)
        {
            return HookResult.Continue;
        }

        PublishRoundInformation();

        MatchData? matchData = match.GetMatchData();
        MatchMap? currentMap = match.GetCurrentMap();
        if (matchData == null || currentMap == null)
        {
            return HookResult.Continue;
        }

        if (_environmentService.isOnGameServerNode())
        {
            match.delayChangeMap(matchData.options.tv_delay);

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

            return HookResult.Continue;
        }

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

                    match.delayChangeMap(Math.Min(5, matchData.options.tv_delay - 15));
                });
            }
        );

        return HookResult.Continue;
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
}
