using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    CsTeam roundWinner;
    int timeoutGivenForOvertime;

    [GameEventHandler]
    public HookResult OnRoundOfficiallyEnded(EventRoundOfficiallyEnded @event, GameEventInfo info)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();
        MatchMap? currentMap = match?.GetCurrentMap();

        if (match == null || currentMap == null || matchData?.current_match_map_id == null)
        {
            return HookResult.Continue;
        }

        if (match.isOverTime())
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

        roundWinner = TeamUtility.TeamNumToCSTeam(@event.Winner);

        if (match == null || matchData == null || currentMap == null)
        {
            return HookResult.Continue;
        }

        if (match.IsKnife())
        {
            match.knifeSystem.SetWinningTeam(TeamUtility.TeamNumToCSTeam(@event.Winner));
        }

        return HookResult.Continue;
    }

    public void PublishRoundInformation(bool SendBackupRound = false)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();
        MatchMap? currentMap = match?.GetCurrentMap();

        if (match == null || matchData == null || currentMap == null)
        {
            return;
        }

        int totalRoundsPlayed = _gameServer.GetTotalRoundsPlayed();

        CsTeam lineup1Side = TeamUtility.GetLineupSide(
            matchData,
            currentMap,
            matchData.lineup_1_id,
            totalRoundsPlayed
        );
        CsTeam lineup2Side = TeamUtility.GetLineupSide(
            matchData,
            currentMap,
            matchData.lineup_2_id,
            totalRoundsPlayed
        );

        _matchEvents.PublishGameEvent(
            "score",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", currentMap.id },
                { "round", totalRoundsPlayed },
                {
                    "lineup_1_score",
                    $"{TeamUtility.GetTeamScore(matchData, currentMap, matchData.lineup_1_id, totalRoundsPlayed)}"
                },
                {
                    "lineup_1_money",
                    $"{TeamUtility.GetTeamMoney(matchData, currentMap, matchData.lineup_1_id, totalRoundsPlayed)}"
                },
                {
                    "lineup_1_timeouts_available",
                    $"{currentMap?.lineup_1_timeouts_available ?? 0}"
                },
                {
                    "lineup_2_score",
                    $"{TeamUtility.GetTeamScore(matchData, currentMap, matchData.lineup_2_id, totalRoundsPlayed)}"
                },
                {
                    "lineup_2_money",
                    $"{TeamUtility.GetTeamMoney(matchData, currentMap, matchData.lineup_2_id, totalRoundsPlayed)}"
                },
                {
                    "lineup_2_timeouts_available",
                    $"{currentMap?.lineup_2_timeouts_available ?? 0}"
                },
                { "lineup_1_side", $"{TeamUtility.CSTeamToString(lineup1Side)}" },
                { "lineup_2_side", $"{TeamUtility.CSTeamToString(lineup2Side)}" },
                {
                    "winning_side",
                    totalRoundsPlayed == 0 ? "None" : $"{TeamUtility.CSTeamToString(roundWinner)}"
                },
                {
                    "backup_file",
                    SendBackupRound
                        ? (_gameBackupRounds.GetBackupRoundFile(totalRoundsPlayed) ?? "")
                        : ""
                },
            }
        );
    }
}
