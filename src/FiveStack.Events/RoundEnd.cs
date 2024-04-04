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
        MatchMap? currentMap = match?.GetCurrentMap();

        if (match == null)
        {
            return HookResult.Continue;
        }

        _ = _gameBackupRounds.UploadBackupRound((_gameServer.GetCurrentRound() - 1).ToString());
        if (match != null && currentMap != null && match.isOverTime())
        {
            match.UpdateMapStatus(eMapStatus.Overtime);
            if (timeoutGivenForOvertime != match.GetOverTimeNumber())
            {
                timeoutGivenForOvertime = match.GetOverTimeNumber();

                _matchEvents.PublishGameEvent(
                    "techTimeout",
                    new Dictionary<string, object>
                    {
                        { "map_id", currentMap.id },
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
        FiveStackMatch? matchData = match?.GetMatchData();

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
                { "lineup_1_score", $"{GetTeamScore(matchData.lineup_1.name)}" },
                { "lineup_1_money", $"{GetTeamMoney(matchData.lineup_1.name)}" },
                {
                    "lineup_1_timeouts_available",
                    $"{currentMap?.lineup_1_timeouts_available ?? 0}"
                },
                { "lineup_2_score", $"{GetTeamScore(matchData.lineup_2.name)}" },
                { "lineup_2_money", $"{GetTeamMoney(matchData.lineup_2.name)}" },
                {
                    "lineup_2_timeouts_available",
                    $"{currentMap?.lineup_2_timeouts_available ?? 0}"
                },
            }
        );

        return HookResult.Continue;
    }

    public int GetTeamScore(string teamName)
    {
        var teamManagers = CounterStrikeSharp.API.Utilities.FindAllEntitiesByDesignerName<CCSTeam>(
            "cs_team_manager"
        );

        foreach (var teamManager in teamManagers)
        {
            if (teamManager.ClanTeamname == teamName)
            {
                return teamManager.Score;
            }
        }

        return 0;
    }

    public int GetTeamMoney(string teamName)
    {
        int totalCash = 0;
        var teamManagers = CounterStrikeSharp.API.Utilities.FindAllEntitiesByDesignerName<CCSTeam>(
            "cs_team_manager"
        );

        foreach (var teamManager in teamManagers)
        {
            if (teamManager.ClanTeamname == teamName)
            {
                foreach (var player in teamManager.PlayerControllers)
                {
                    totalCash += (
                        CounterStrikeSharp
                            .API.Utilities.GetPlayerFromIndex((int)player.Index)
                            ?.InGameMoneyServices?.Account ?? 0
                    );
                }
            }
        }

        return totalCash;
    }
}
