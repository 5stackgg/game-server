using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
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

        match.UpdateMapStatus(eMapStatus.UploadingDemo);

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

        CsTeam lineup1Side = CsTeam.None;
        CsTeam lineup2Side = CsTeam.None;
        foreach (var team in MatchUtility.Teams())
        {
            if (team.ClanTeamname == matchData.lineup_1.name)
            {
                lineup1Side = TeamUtility.TeamNumToCSTeam(team.TeamNum);
            }
            else if (team.ClanTeamname == matchData.lineup_2.name)
            {
                lineup2Side = TeamUtility.TeamNumToCSTeam(team.TeamNum);
            }
        }

        _matchEvents.PublishGameEvent(
            "score",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", currentMap.id },
                { "round", _gameServer.GetCurrentRound() },
                { "lineup_1_score", $"{TeamUtility.GetTeamScore(matchData.lineup_1.name)}" },
                { "lineup_1_money", $"{TeamUtility.GetTeamMoney(matchData.lineup_1.name)}" },
                {
                    "lineup_1_timeouts_available",
                    $"{currentMap?.lineup_1_timeouts_available ?? 0}"
                },
                { "lineup_2_score", $"{TeamUtility.GetTeamScore(matchData.lineup_2.name)}" },
                { "lineup_2_money", $"{TeamUtility.GetTeamMoney(matchData.lineup_2.name)}" },
                {
                    "lineup_2_timeouts_available",
                    $"{currentMap?.lineup_2_timeouts_available ?? 0}"
                },
                { "lineup_1_side", $"{TeamUtility.CSTeamToString(lineup1Side)}" },
                { "lineup_2_side", $"{TeamUtility.CSTeamToString(lineup2Side)}" },
                { "winning_side", $"{TeamUtility.CSTeamToString(this.roundWinner)}" },
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
}
