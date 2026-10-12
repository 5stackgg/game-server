/**
 * 5Stack CS:GO Plugin - Match Manager
 * State machine: Scheduled->Warmup->Knife->Live->Overtime->Finished
 */

#if defined _fivestack_match_manager_included
    #endinput
#endif
#define _fivestack_match_manager_included

stock void MatchManager_SetupMatch()
{
    if (!g_bMatchLoaded)
        return;

    LogMessage("[5Stack] Setup Match %s", g_MatchData.id);

    g_iCurrentMapIndex = GetCurrentMapIndex();
    if (g_iCurrentMapIndex < 0)
    {
        LogError("[5Stack] Match does not have a current map");
        return;
    }

    char currentMapName[MAX_MAP_NAME_LENGTH];
    GetCurrentMap(currentMapName, sizeof(currentMapName));

    char expectedMapName[MAX_MAP_NAME_LENGTH];
    strcopy(expectedMapName, sizeof(expectedMapName), g_MatchData.match_maps[g_iCurrentMapIndex].map.name);

    LogMessage("[5Stack] Game State: %s, expected map: %s, current map: %s",
        g_MatchData.match_maps[g_iCurrentMapIndex].status,
        expectedMapName, currentMapName);

    // Check if we need to change map
    if (g_MatchData.match_maps[g_iCurrentMapIndex].map.workshop_map_id[0] != '\0')
    {
        // Workshop map - would need special handling for CS:GO
        // For now fall through to name check
    }

    if (StrContains(currentMapName, expectedMapName, false) == -1)
    {
        MatchManager_ChangeMap();
        return;
    }

    if (IsMapFinished())
    {
        LogMessage("[5Stack] Map already finished");
        return;
    }

    // Execute match type config
    char execCmd[128];
    FormatEx(execCmd, sizeof(execCmd), "exec 5stack.%s.cfg", g_MatchData.options.type);
    StringToLower(execCmd);
    SendCommands(execCmd);

    if (g_MatchData.is_lan)
    {
        SendCommands("exec 5stack.lan.cfg");
    }

    // Set password
    char passwordCmd[128];
    FormatEx(passwordCmd, sizeof(passwordCmd), "sv_password \"%s\"", g_MatchData.password);
    SendCommands(passwordCmd);

    // Set match restart delay
    char delayCmd[64];
    FormatEx(delayCmd, sizeof(delayCmd), "mp_match_restart_delay %d", g_MatchData.options.tv_delay);
    SendCommands(delayCmd);

    // Setup game mode
    MatchManager_SetupGameMode();

    // Transition to the map's current status
    eMapStatus targetStatus = MapStatusStringToEnum(g_MatchData.match_maps[g_iCurrentMapIndex].status);
    if (targetStatus != g_CurrentMapStatus)
    {
        MatchManager_UpdateMapStatus(targetStatus);
    }

    if (IsWarmup())
    {
        AlertAll("Match data received");
    }

    // Setup TV
    char tvCmd[64];
    FormatEx(tvCmd, sizeof(tvCmd), "tv_delay %d", g_MatchData.options.tv_delay);
    SendCommands(tvCmd);

    // Kick bots
    SendCommands("bot_kick");
}

stock void MatchManager_SetupGameMode()
{
    if (!g_bMatchLoaded) return;

    if (StrEqual(g_MatchData.options.type, "Wingman") || StrEqual(g_MatchData.options.type, "Duel"))
    {
        SendCommands("game_type 0; game_mode 2");
    }
    else
    {
        SendCommands("game_type 0; game_mode 1");
    }
}

stock void MatchManager_SetupTeams()
{
    if (!g_bMatchLoaded || g_iCurrentMapIndex < 0) return;

    int lineup1StartingSide = TeamStringToCSTeam(g_MatchData.match_maps[g_iCurrentMapIndex].lineup_1_side);

    // mp_teamname_1 = CT team name, mp_teamname_2 = T team name
    char cmd[256];
    if (lineup1StartingSide == CS_TEAM_CT)
    {
        FormatEx(cmd, sizeof(cmd), "mp_teamname_1 \"%s\"; mp_teamname_2 \"%s\"",
            g_MatchData.lineup_1.name, g_MatchData.lineup_2.name);
    }
    else
    {
        FormatEx(cmd, sizeof(cmd), "mp_teamname_1 \"%s\"; mp_teamname_2 \"%s\"",
            g_MatchData.lineup_2.name, g_MatchData.lineup_1.name);
    }
    SendCommands(cmd);

    // Enforce all player teams after a short delay
    CreateTimer(1.0, Timer_EnforceTeams);
}

public Action Timer_EnforceTeams(Handle timer)
{
    for (int i = 1; i <= MaxClients; i++)
    {
        if (IsValidClient(i))
        {
            EnforceMemberTeam(i);
        }
    }
    return Plugin_Stop;
}

stock void MatchManager_UpdateMapStatus(eMapStatus newStatus, const char[] winningLineupId = "")
{
    if (!g_bMatchLoaded) return;
    if (g_CurrentMapStatus == newStatus) return;

    char oldStr[MAX_STATUS_LENGTH], newStr[MAX_STATUS_LENGTH];
    MapStatusToString(g_CurrentMapStatus, oldStr, sizeof(oldStr));
    MapStatusToString(newStatus, newStr, sizeof(newStr));
    LogMessage("[5Stack] Map status: %s -> %s", oldStr, newStr);

    switch (newStatus)
    {
        case MapStatus_Scheduled:
        {
            MatchManager_UpdateMapStatus(MapStatus_Warmup);
            return;
        }
        case MapStatus_Warmup:
        {
            MatchManager_SetupTeams();
            MatchManager_StartWarmup();
        }
        case MapStatus_Knife:
        {
            if (!g_MatchData.options.knife_round)
            {
                MatchManager_UpdateMapStatus(MapStatus_Live);
                return;
            }
            if (g_iCurrentMapIndex >= 0 &&
                g_MatchData.match_maps[g_iCurrentMapIndex].order == g_MatchData.options.best_of)
            {
                MatchManager_StartKnife();
            }
            else
            {
                MatchManager_UpdateMapStatus(MapStatus_Live);
                return;
            }
        }
        case MapStatus_Paused:
        {
            if (IsWarmup())
            {
                SendCommands("mp_warmup_end");
            }
            MatchManager_PauseMatch("", true);
        }
        case MapStatus_Live:
        {
            MatchManager_StartLive();
        }
        case MapStatus_Finished, MapStatus_Surrendered:
        {
            Demo_Stop();
        }
        case MapStatus_UploadingDemo:
        {
            Demo_Stop();
            if (g_CurrentMapStatus == MapStatus_Unknown || IsMapFinished())
                return;
        }
    }

    // Publish status change to backend
    PublishMapStatus(newStatus, winningLineupId);
    g_CurrentMapStatus = newStatus;
}

stock void MatchManager_StartWarmup()
{
    SendCommands("exec 5stack.warmup.cfg");
    g_bKnifeRoundActive = false;

    if (!g_bMatchLoaded) return;

    // Ensure warmup mode is active
    if (GameRules_GetProp("m_bWarmupPeriod") == 0)
    {
        SendCommands("mp_warmup_start");
    }

    Ready_Setup();
}

stock void MatchManager_StartKnife()
{
    if (!g_bMatchLoaded || IsKnife()) return;

    Captain_AutoSelect();
    Knife_Start();
}

stock void MatchManager_StartLive()
{
    g_bKnifeRoundActive = false;

    if (!g_bMatchLoaded) return;

    LogMessage("[5Stack] Starting Live Match");

    // Configure live match settings
    char cmds[512];
    FormatEx(cmds, sizeof(cmds),
        "mp_backup_round_auto 1; mp_maxrounds %d; mp_overtime_enable %d",
        g_MatchData.options.mr * 2,
        g_MatchData.options.overtime ? 1 : 0);
    SendCommands(cmds);

    // Execute match type config
    char execCmd[128];
    FormatEx(execCmd, sizeof(execCmd), "exec 5stack.%s.cfg", g_MatchData.options.type);
    StringToLower(execCmd);
    SendCommands(execCmd);

    // Start demo recording
    Demo_Start();

    // End warmup and start live
    CreateTimer(0.5, Timer_StartLiveGame);
}

public Action Timer_StartLiveGame(Handle timer)
{
    MatchManager_ResumeMatch("", true);

    if (IsWarmup() || IsKnife())
    {
        SendCommands("mp_restartgame 1; mp_warmup_end");
    }
    return Plugin_Stop;
}

stock void MatchManager_PauseMatch(const char[] message = "", bool skipUpdate = false)
{
    SendCommands("mp_pause_match");

    if (IsPaused()) return;

    if (message[0] != '\0')
    {
        AlertAll(message);
    }

    // Resume hint timer
    if (g_hResumeMessageTimer != INVALID_HANDLE)
    {
        KillTimer(g_hResumeMessageTimer);
    }
    g_hResumeMessageTimer = CreateTimer(3.0, Timer_ResumeHint, _, TIMER_REPEAT);

    if (!skipUpdate)
    {
        MatchManager_UpdateMapStatus(MapStatus_Paused);
    }
}

public Action Timer_ResumeHint(Handle timer)
{
    if (!IsFreezePeriod() || !IsPaused())
        return Plugin_Continue;

    AlertAll("Type .resume in chat to unpause");
    return Plugin_Continue;
}

stock void MatchManager_ResumeMatch(const char[] message = "", bool skipUpdate = false)
{
    SendCommands("mp_unpause_match");

    if (g_hResumeMessageTimer != INVALID_HANDLE)
    {
        KillTimer(g_hResumeMessageTimer);
        g_hResumeMessageTimer = INVALID_HANDLE;
    }

    // Cancel any active resume votes
    if (g_bVoteActive)
    {
        Vote_Cancel();
    }

    if (!IsPaused()) return;

    if (message[0] != '\0')
    {
        AlertAll(message);
    }

    if (!skipUpdate)
    {
        MatchManager_UpdateMapStatus(IsOvertime() ? MapStatus_Overtime : MapStatus_Live);
    }
}

stock void MatchManager_ChangeMap()
{
    if (g_hMapChangeCountdownTimer != INVALID_HANDLE) return;
    if (!g_bMatchLoaded || g_iCurrentMapIndex < 0) return;

    Globals_Reset();

    char mapName[MAX_MAP_NAME_LENGTH];
    strcopy(mapName, sizeof(mapName), g_MatchData.match_maps[g_iCurrentMapIndex].map.name);

    if (g_MatchData.match_maps[g_iCurrentMapIndex].map.workshop_map_id[0] != '\0')
    {
        char cmd[128];
        FormatEx(cmd, sizeof(cmd), "host_workshop_map %s", g_MatchData.match_maps[g_iCurrentMapIndex].map.workshop_map_id);
        LogMessage("[5Stack] Changing to workshop map: %s", g_MatchData.match_maps[g_iCurrentMapIndex].map.workshop_map_id);
        SendCommands(cmd);
    }
    else
    {
        char cmd[128];
        FormatEx(cmd, sizeof(cmd), "changelevel \"%s\"", mapName);
        LogMessage("[5Stack] Changing map to: %s", mapName);
        SendCommands(cmd);
    }
}

stock void MatchManager_DelayChangeMap(int delay)
{
    g_iMapChangeDelay = delay;
    LogMessage("[5Stack] Delaying map change by %d seconds", delay);

    if (g_hMapChangeTimer != INVALID_HANDLE)
    {
        KillTimer(g_hMapChangeTimer);
    }

    g_hMapChangeTimer = CreateTimer(float(delay), Timer_MapChangeComplete);
    g_hMapChangeCountdownTimer = CreateTimer(1.0, Timer_MapChangeCountdown, _, TIMER_REPEAT);
}

public Action Timer_MapChangeComplete(Handle timer)
{
    g_hMapChangeTimer = INVALID_HANDLE;
    if (g_hMapChangeCountdownTimer != INVALID_HANDLE)
    {
        KillTimer(g_hMapChangeCountdownTimer);
        g_hMapChangeCountdownTimer = INVALID_HANDLE;
    }
    LogMessage("[5Stack] Map change delay complete");
    MatchService_FetchMatch();
    return Plugin_Stop;
}

public Action Timer_MapChangeCountdown(Handle timer)
{
    if (g_iMapChangeDelay > 0)
    {
        AlertAll("Next map in %d seconds...", g_iMapChangeDelay);
        g_iMapChangeDelay--;
        return Plugin_Continue;
    }
    g_hMapChangeCountdownTimer = INVALID_HANDLE;
    return Plugin_Stop;
}

stock void MatchManager_Reset()
{
    LogMessage("[5Stack] Resetting match state");
    Globals_Reset();
}

// Helper to lowercase a string in-place
stock void StringToLower(char[] str)
{
    int i = 0;
    while (str[i] != '\0')
    {
        if (str[i] >= 'A' && str[i] <= 'Z')
            str[i] += 32;
        i++;
    }
}
