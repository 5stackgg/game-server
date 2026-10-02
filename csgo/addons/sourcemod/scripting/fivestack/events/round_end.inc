/**
 * 5Stack CS:GO Plugin - Round End Events
 * round_end -> score + win reason
 * round_officially_ended -> OT detection, timeout grants
 */

#if defined _fivestack_event_round_end_included
    #endinput
#endif
#define _fivestack_event_round_end_included

public Action Event_RoundEnd(Event event, const char[] name, bool dontBroadcast)
{
    // During knife round: detect winner
    if (IsKnife())
    {
        int winner = event.GetInt("winner");
        if (winner == CS_TEAM_T || winner == CS_TEAM_CT)
        {
            Knife_SetWinner(winner);
        }
        return Plugin_Continue;
    }

    if (!IsLive()) return Plugin_Continue;

    char message[128];
    event.GetString("message", message, sizeof(message));

    eWinReason reason = WinReasonFromMessage(message);
    PublishRoundInformation(reason);

    return Plugin_Continue;
}

public Action Event_RoundOfficiallyEnded(Event event, const char[] name, bool dontBroadcast)
{
    if (!IsLive()) return Plugin_Continue;

    // Check for overtime transition
    if (IsOvertime() && g_CurrentMapStatus != MapStatus_Overtime)
    {
        MatchManager_UpdateMapStatus(MapStatus_Overtime);
    }

    // Check if new OT half started — reset tactical timeouts to 1 each
    if (IsOvertime() && g_bMatchLoaded && g_iCurrentMapIndex >= 0)
    {
        int totalRounds = GetTotalRoundsPlayed();
        int mr = g_MatchData.options.mr;

        if (totalRounds > mr * 2)
        {
            ConVar cvOtMaxRounds = FindConVar("mp_overtime_maxrounds");
            int otMr = (cvOtMaxRounds != null) ? cvOtMaxRounds.IntValue : 6;

            int otRound = totalRounds - (mr * 2);
            // At the start of each OT half, reset timeouts
            if (otRound % (otMr / 2) == 0)
            {
                g_MatchData.match_maps[g_iCurrentMapIndex].lineup_1_timeouts_available = 1;
                g_MatchData.match_maps[g_iCurrentMapIndex].lineup_2_timeouts_available = 1;
            }
        }
    }

    return Plugin_Continue;
}
