using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using FiveStack.Entities;
using FiveStack.Enums;
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

        match.UpdateMapStatus(eMapStatus.UploadingDemo, _matchEvents.GetWinningLineupId());

        MatchData? matchData = match.GetMatchData();
        MatchMap? currentMap = match.GetCurrentMap();

        if (currentMap == null || matchData?.current_match_map_id == null)
        {
            return HookResult.Continue;
        }

        // Handle offline mode map progression
        if (_environmentService.IsOfflineMode())
        {
            HandleOfflineMapProgression(match, matchData, currentMap);
        }
        else if (matchData.options.tv_delay > 0)
        {
            match.delayChangeMap(matchData.options.tv_delay);
        }

        PublishRoundInformation();

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
}
