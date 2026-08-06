/**
 * 5Stack CS:GO Plugin - Match Events
 * HTTP POST event publishing with retry queue.
 */

#if defined _fivestack_match_events_included
    #endinput
#endif
#define _fivestack_match_events_included

#define EVENT_RETRY_INTERVAL 5.0

stock void MatchEvents_Init()
{
    if (g_hEventRetryTimer != INVALID_HANDLE)
    {
        KillTimer(g_hEventRetryTimer);
    }
    g_hEventRetryTimer = CreateTimer(EVENT_RETRY_INTERVAL, Timer_RetryEvents, _, TIMER_REPEAT);
}

stock void PublishMapStatus(eMapStatus status, const char[] winningLineupId = "")
{
    if (!Config_IsValid()) return;

    char statusStr[MAX_STATUS_LENGTH];
    MapStatusToString(status, statusStr, sizeof(statusStr));

    char data[512];
    FormatEx(data, sizeof(data),
        "{\"status\":\"%s\",\"winning_lineup_id\":\"%s\"}",
        statusStr, winningLineupId);

    PublishGameEvent("mapStatus", data);
}

// Publish a game event to the backend via HTTP POST.
// dataJson should be a JSON object string like {"key":"value",...}
stock void PublishGameEvent(const char[] eventName, const char[] dataJson)
{
    if (!Config_IsValid() || !g_bMatchLoaded)
        return;

    if (g_iCurrentMapIndex < 0)
        return;

    char messageId[GUID_LENGTH];
    GenerateUUID(messageId, sizeof(messageId));

    // Build the full event payload
    char payload[8192];
    FormatEx(payload, sizeof(payload),
        "{\"event\":\"events\",\"data\":{\"matchId\":\"%s\",\"mapId\":\"%s\",\"messageId\":\"%s\",\"event\":\"%s\",\"data\":%s}}",
        g_MatchData.id,
        g_MatchData.match_maps[g_iCurrentMapIndex].id,
        messageId,
        eventName,
        dataJson);

    // Send immediately
    SendEventHTTP(payload);

    // Also queue for retry in case of failure
    g_alPendingEvents.PushString(payload);
}

stock void SendEventHTTP(const char[] payload)
{
    HTTPClient client = CreateApiClient();

    JSONObject json = JSONObject.FromString(payload);
    if (json == null)
    {
        LogError("[5Stack] Failed to parse event payload as JSON");
        return;
    }

    client.Post("game-server-node/events", json, OnEventSent);
    delete json;
}

public void OnEventSent(HTTPResponse response, any value)
{
    if (response.Status == HTTPStatus_OK)
    {
        // Success — remove from pending queue (next retry tick cleans stale entries)
        return;
    }

    LogError("[5Stack] Event POST failed: HTTP %d", response.Status);
}

public Action Timer_RetryEvents(Handle timer)
{
    if (g_alPendingEvents == null || g_alPendingEvents.Length == 0)
        return Plugin_Continue;

    // Try to send the first pending event
    char payload[8192];
    g_alPendingEvents.GetString(0, payload, sizeof(payload));
    g_alPendingEvents.Erase(0);

    SendEventHTTP(payload);

    return Plugin_Continue;
}

// Publish round score information
stock void PublishRoundInformation(eWinReason winReason = WinReason_Unknown, const char[] backupFile = "")
{
    if (!g_bMatchLoaded || g_iCurrentMapIndex < 0 || !IsLive())
        return;

    int totalRounds = GetTotalRoundsPlayed();
    int lineup1Side = GetLineupSide(g_MatchData.lineup_1_id, totalRounds);
    int lineup2Side = GetLineupSide(g_MatchData.lineup_2_id, totalRounds);

    int lineup1Score = GetLineupScore(g_MatchData.lineup_1_id);
    int lineup2Score = GetLineupScore(g_MatchData.lineup_2_id);

    int lineup1Money = GetTeamMoney(lineup1Side);
    int lineup2Money = GetTeamMoney(lineup2Side);

    char winReasonStr[32];
    WinReasonToString(winReason, winReasonStr, sizeof(winReasonStr));

    char lineup1SideStr[16], lineup2SideStr[16];
    CSTeamToString(lineup1Side, lineup1SideStr, sizeof(lineup1SideStr));
    CSTeamToString(lineup2Side, lineup2SideStr, sizeof(lineup2SideStr));

    char winningSide[16] = "";
    if (winReason == WinReason_TerroristsWin || winReason == WinReason_BombExploded)
        strcopy(winningSide, sizeof(winningSide), "TERRORIST");
    else if (winReason != WinReason_Unknown)
        strcopy(winningSide, sizeof(winningSide), "CT");

    char data[2048];
    FormatEx(data, sizeof(data),
        "{\"match_map_id\":\"%s\",\"round\":%d,"
        "\"lineup_1_score\":%d,\"lineup_2_score\":%d,"
        "\"lineup_1_money\":%d,\"lineup_2_money\":%d,"
        "\"lineup_1_timeouts_available\":%d,\"lineup_2_timeouts_available\":%d,"
        "\"lineup_1_side\":\"%s\",\"lineup_2_side\":\"%s\","
        "\"winning_side\":\"%s\",\"winning_reason\":\"%s\","
        "\"backup_file\":\"%s\"}",
        g_MatchData.match_maps[g_iCurrentMapIndex].id,
        GetCurrentRound(),
        lineup1Score, lineup2Score,
        lineup1Money, lineup2Money,
        g_MatchData.match_maps[g_iCurrentMapIndex].lineup_1_timeouts_available,
        g_MatchData.match_maps[g_iCurrentMapIndex].lineup_2_timeouts_available,
        lineup1SideStr, lineup2SideStr,
        winningSide, winReasonStr,
        backupFile);

    PublishGameEvent("score", data);
}
