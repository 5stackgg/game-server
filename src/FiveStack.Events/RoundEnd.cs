using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    // int timeoutGivenForOvertime = 0;

    [GameEventHandler]
    public HookResult OnRoundOfficiallyEnded(EventRoundOfficiallyEnded @event, GameEventInfo info)
    {
        // _ = UploadBackupRound(_currentRound.ToString());
        // UpdateCurrentRound();

        // if (_matchData != null && _currentMap != null && isOverTime())
        // {
        //     UpdateMapStatus(eMapStatus.Overtime);
        //     if (timeoutGivenForOvertime != GetOverTimeNumber())
        //     {
        //         timeoutGivenForOvertime = GetOverTimeNumber();

        //         PublishGameEvent(
        //             "techTimeout",
        //             new Dictionary<string, object>
        //             {
        //                 { "map_id", _currentMap.id },
        //                 { "lineup_1_timeouts_available", 1 },
        //                 { "lineup_2_timeouts_available", 1 },
        //             }
        //         );
        //     }
        // }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        // if (_matchData == null || _matchData.current_match_map_id == null || IsKnife())
        // {
        //     Logger.LogInformation($"TEAM ASSIGNED {@event.Winner}");

        //     KnifeWinningTeam = TeamNumToCSTeam(@event.Winner);

        //     NotifyCaptainSideSelection();

        //     return HookResult.Continue;
        // }

        // if (!IsLive())
        // {
        //     return HookResult.Continue;
        // }

        // PublishGameEvent(
        //     "score",
        //     new Dictionary<string, object>
        //     {
        //         { "time", DateTime.Now },
        //         { "match_map_id", _matchData.current_match_map_id },
        //         { "round", _currentRound + 1 },
        //         { "lineup_1_score", $"{GetTeamScore(_matchData.lineup_1.name)}" },
        //         { "lineup_1_money", $"{GetTeamMoney(_matchData.lineup_1.name)}" },
        //         {
        //             "lineup_1_timeouts_available",
        //             $"{_currentMap?.lineup_1_timeouts_available ?? 0}"
        //         },
        //         { "lineup_2_score", $"{GetTeamScore(_matchData.lineup_2.name)}" },
        //         { "lineup_2_money", $"{GetTeamMoney(_matchData.lineup_2.name)}" },
        //         {
        //             "lineup_2_timeouts_available",
        //             $"{_currentMap?.lineup_2_timeouts_available ?? 0}"
        //         },
        //     }
        // );

        return HookResult.Continue;
    }

    public int GetTeamScore(string teamName)
    {
        // if (_matchData == null)
        // {
        //     return 0;
        // }

        // var teamManagers = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");

        // foreach (var teamManager in teamManagers)
        // {
        //     if (teamManager.ClanTeamname == teamName)
        //     {
        //         return teamManager.Score;
        //     }
        // }

        return 0;
    }

    public int GetTeamMoney(string teamName)
    {
        // if (_matchData == null)
        // {
        //     return 0;
        // }

        // int totalCash = 0;
        // var teamManagers = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");

        // foreach (var teamManager in teamManagers)
        // {
        //     if (teamManager.ClanTeamname == teamName)
        //     {
        //         foreach (var player in teamManager.PlayerControllers)
        //         {
        //             totalCash += (
        //                 Utilities
        //                     .GetPlayerFromIndex((int)player.Index)
        //                     ?.InGameMoneyServices?.Account ?? 0
        //             );
        //         }
        //     }
        // }

        // return totalCash;
        return 0;
    }

    // public void NotifyCaptainSideSelection()
    // {
    //     if (KnifeWinningTeam == null)
    //     {
    //         return;
    //     }

    //     CsTeam knifeTeam =
    //         KnifeWinningTeam == CsTeam.Terrorist ? CsTeam.Terrorist : CsTeam.CounterTerrorist;

    //     Message(
    //         HudDestination.Chat,
    //         $"As the captain you must select to {ChatColors.Green}.stay {ChatColors.Default} or {ChatColors.Green}.switch",
    //         _captains[knifeTeam]
    //     );
    //     Message(
    //         HudDestination.Alert,
    //         $"{(KnifeWinningTeam == CsTeam.Terrorist ? "Terrorist" : "CT")} - Captain is Picking Sides!"
    //     );
    // }
}
