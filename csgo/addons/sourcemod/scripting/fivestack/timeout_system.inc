/**
 * 5Stack CS:GO Plugin - Timeout System
 * Tech pause, tactical timeout, resume logic.
 */

#if defined _fivestack_timeout_system_included
    #endinput
#endif
#define _fivestack_timeout_system_included

stock void Timeout_RequestPause(int client)
{
    if (!IsLive())
    {
        MessageClient(client, "Cannot pause: match is not live.");
        return;
    }

    if (IsPaused())
    {
        MessageClient(client, "Match is already paused.");
        return;
    }

    if (!g_bMatchLoaded) return;

    // Check permission
    bool canPause = false;
    eTimeoutSettings setting = TimeoutSettingStringToEnum(g_MatchData.options.tech_timeout_setting);

    if (IsPlayerPrivileged(client))
    {
        canPause = true;
    }
    else
    {
        switch (setting)
        {
            case TimeoutSetting_Coach:
                canPause = false; // Coach-only — non-coaches go to vote
            case TimeoutSetting_CoachAndPlayers:
                canPause = true;
            case TimeoutSetting_CoachAndCaptains:
                canPause = Captain_IsCaptain(client, GetClientTeam(client));
            case TimeoutSetting_Admin:
                canPause = false;
        }
    }

    if (canPause)
    {
        MessageAll("%N has paused the match.", client);
        MatchManager_PauseMatch("Technical pause");
    }
    else
    {
        // Start a vote
        Vote_Start("Technical Pause", CS_TEAM_NONE, Vote_PauseSuccess, Vote_PauseFail, true, 30.0);
    }
}

public void Vote_PauseSuccess()
{
    MessageAll("Technical pause vote passed.");
    MatchManager_PauseMatch("Technical pause (voted)");
}

public void Vote_PauseFail()
{
    MessageAll("Technical pause vote failed.");
}

stock void Timeout_RequestResume(int client)
{
    if (!IsPaused())
    {
        MessageClient(client, "Match is not paused.");
        return;
    }

    if (IsPlayerPrivileged(client))
    {
        MessageAll("%N has resumed the match.", client);
        MatchManager_ResumeMatch("Match resumed");
    }
    else
    {
        // Start a vote to resume
        Vote_Start("Resume match", CS_TEAM_NONE, Vote_ResumeSuccess, Vote_ResumeFail, true, 30.0);
    }
}

public void Vote_ResumeSuccess()
{
    MessageAll("Resume vote passed.");
    MatchManager_ResumeMatch("Match resumed (voted)");
}

public void Vote_ResumeFail()
{
    MessageAll("Resume vote failed.");
}

stock void Timeout_CallTactical(int client)
{
    if (!IsLive())
    {
        MessageClient(client, "Cannot call timeout: match is not live.");
        return;
    }

    if (!g_bMatchLoaded || g_iCurrentMapIndex < 0) return;

    int team = GetClientTeam(client);

    // Check permission
    eTimeoutSettings setting = TimeoutSettingStringToEnum(g_MatchData.options.timeout_setting);
    bool canTimeout = false;

    if (IsPlayerPrivileged(client))
    {
        canTimeout = true;
    }
    else
    {
        switch (setting)
        {
            case TimeoutSetting_Coach:
                canTimeout = false;
            case TimeoutSetting_CoachAndPlayers:
                canTimeout = true;
            case TimeoutSetting_CoachAndCaptains:
                canTimeout = Captain_IsCaptain(client, team);
            case TimeoutSetting_Admin:
                canTimeout = false;
        }
    }

    if (!canTimeout)
    {
        MessageClient(client, "You don't have permission to call a timeout.");
        return;
    }

    // Check timeouts available
    bool isLineup1 = false;
    char lineupId[GUID_LENGTH];
    if (GetPlayerLineupId(client, lineupId, sizeof(lineupId)))
    {
        isLineup1 = StrEqual(lineupId, g_MatchData.lineup_1_id) ||
                     StrEqual(lineupId, g_MatchData.lineup_1.id);
    }

    int available;
    if (isLineup1)
        available = g_MatchData.match_maps[g_iCurrentMapIndex].lineup_1_timeouts_available;
    else
        available = g_MatchData.match_maps[g_iCurrentMapIndex].lineup_2_timeouts_available;

    if (available <= 0)
    {
        MessageClient(client, "Your team has no timeouts remaining.");
        return;
    }

    // Decrement timeout counter
    if (isLineup1)
        g_MatchData.match_maps[g_iCurrentMapIndex].lineup_1_timeouts_available--;
    else
        g_MatchData.match_maps[g_iCurrentMapIndex].lineup_2_timeouts_available--;

    // Call the tactical timeout
    if (team == CS_TEAM_T)
        SendCommands("timeout_terrorist_start");
    else
        SendCommands("timeout_ct_start");

    MessageAll("%N called a tactical timeout (%d remaining).", client, available - 1);

    // Publish event
    char steamId[MAX_STEAMID_LENGTH];
    GetClientAuthId(client, AuthId_SteamID64, steamId, sizeof(steamId));

    char data[256];
    FormatEx(data, sizeof(data), "{\"steam_id\":\"%s\"}", steamId);
    PublishGameEvent("techTimeout", data);
}

stock bool Timeout_IsActive()
{
    return (GameRules_GetProp("m_bTerroristTimeOutActive") != 0 ||
            GameRules_GetProp("m_bCTTimeOutActive") != 0);
}
