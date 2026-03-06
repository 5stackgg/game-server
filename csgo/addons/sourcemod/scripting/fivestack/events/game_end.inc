/**
 * 5Stack CS:GO Plugin - Game End Events
 * cs_win_panel_match -> demo upload + map change
 */

#if defined _fivestack_event_game_end_included
    #endinput
#endif
#define _fivestack_event_game_end_included

// Determine which lineup won based on scores
stock void GetWinningLineupId(char[] buffer, int maxlen)
{
    buffer[0] = '\0';

    if (!g_bMatchLoaded || g_iCurrentMapIndex < 0)
        return;

    int lineup1Score = GetLineupScore(g_MatchData.lineup_1_id);
    int lineup2Score = GetLineupScore(g_MatchData.lineup_2_id);

    if (lineup1Score > lineup2Score)
        strcopy(buffer, maxlen, g_MatchData.lineup_1_id);
    else
        strcopy(buffer, maxlen, g_MatchData.lineup_2_id);
}

public Action Event_CSWinPanelMatch(Event event, const char[] name, bool dontBroadcast)
{
    if (!g_bMatchLoaded) return Plugin_Continue;

    // Publish final scores
    PublishRoundInformation();

    // Stop demo recording before upload
    Demo_Stop();

    // Transition status
    char winningId[GUID_LENGTH];
    GetWinningLineupId(winningId, sizeof(winningId));
    MatchManager_UpdateMapStatus(MapStatus_UploadingDemo, winningId);

    // Upload demo after a delay (wait for tv_delay to flush)
    CreateTimer(15.0, Timer_UploadDemo);

    return Plugin_Continue;
}

public Action Timer_UploadDemo(Handle timer)
{
    Demo_Upload();

    // After upload, transition to finished and schedule map change
    CreateTimer(5.0, Timer_FinishAndChangeMap);
    return Plugin_Stop;
}

public Action Timer_FinishAndChangeMap(Handle timer)
{
    if (!g_bMatchLoaded) return Plugin_Stop;

    char winningId[GUID_LENGTH];
    GetWinningLineupId(winningId, sizeof(winningId));
    MatchManager_UpdateMapStatus(MapStatus_Finished, winningId);

    // Delay map change by tv_delay
    MatchManager_DelayChangeMap(g_MatchData.options.tv_delay);

    return Plugin_Stop;
}
