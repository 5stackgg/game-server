/**
 * 5Stack CS:GO Plugin - Match Commands
 * get_match, match_state (server commands)
 */

#if defined _fivestack_cmd_match_included
    #endinput
#endif
#define _fivestack_cmd_match_included

public Action Command_GetMatch(int args)
{
    LogMessage("[5Stack] Fetching match data from API...");
    MatchService_FetchMatch();
    return Plugin_Handled;
}

public Action Command_MatchState(int args)
{
    char statusStr[MAX_STATUS_LENGTH];
    MapStatusToString(g_CurrentMapStatus, statusStr, sizeof(statusStr));

    PrintToServer("[5Stack] Match loaded: %s", g_bMatchLoaded ? "yes" : "no");

    if (g_bMatchLoaded)
    {
        PrintToServer("[5Stack] Match ID: %s", g_MatchData.id);
        PrintToServer("[5Stack] Map status: %s", statusStr);
        PrintToServer("[5Stack] Map index: %d", g_iCurrentMapIndex);
        PrintToServer("[5Stack] Lineup 1: %s", g_MatchData.lineup_1.name);
        PrintToServer("[5Stack] Lineup 2: %s", g_MatchData.lineup_2.name);
        PrintToServer("[5Stack] Type: %s, MR: %d, BO: %d",
            g_MatchData.options.type, g_MatchData.options.mr, g_MatchData.options.best_of);
    }

    return Plugin_Handled;
}
