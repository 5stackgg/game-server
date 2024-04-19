using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    int timeoutGivenForOvertime;

    [GameEventHandler]
    public HookResult OnRoundOfficiallyEnded(EventRoundOfficiallyEnded @event, GameEventInfo info)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (match == null || matchData?.current_match_map_id == null)
        {
            return HookResult.Continue;
        }

        _ = _gameBackupRounds.UploadBackupRound((_gameServer.GetCurrentRound() - 1).ToString());

        _gameServer.UpdateCurrentRound();

        if (match != null && match.isOverTime())
        {
            match.UpdateMapStatus(eMapStatus.Overtime);
            if (timeoutGivenForOvertime != match.GetOverTimeNumber())
            {
                timeoutGivenForOvertime = match.GetOverTimeNumber();

                _matchEvents.PublishGameEvent(
                    "techTimeout",
                    new Dictionary<string, object>
                    {
                        { "map_id", matchData.current_match_map_id },
                        { "lineup_1_timeouts_available", 1 },
                        { "lineup_2_timeouts_available", 1 },
                    }
                );
            }
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchMap? currentMap = match?.GetCurrentMap();
        MatchData? matchData = match?.GetMatchData();

        if (match == null || matchData == null || currentMap == null)
        {
            return HookResult.Continue;
        }

        if (match.IsKnife())
        {
            match.knifeSystem.SetWinningTeam(TeamUtility.TeamNumToCSTeam(@event.Winner));

            return HookResult.Continue;
        }

        if (!match.IsLive())
        {
            return HookResult.Continue;
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
            }
        );

        return HookResult.Continue;
    }
}
