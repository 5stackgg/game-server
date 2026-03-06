/**
 * 5Stack CS:GO Plugin - Ready System
 * Toggle ready, clan tag display, threshold check.
 */

#if defined _fivestack_ready_system_included
    #endinput
#endif
#define _fivestack_ready_system_included

stock void Ready_Setup()
{
    Ready_Reset();
    g_bReadySystemActive = true;
    g_hReadyTimer = CreateTimer(3.0, Timer_ReadyStatus, _, TIMER_REPEAT);
}

stock void Ready_Reset()
{
    for (int i = 0; i <= MAXPLAYERS; i++)
        g_bReadyPlayers[i] = false;

    if (g_hReadyTimer != INVALID_HANDLE)
    {
        KillTimer(g_hReadyTimer);
        g_hReadyTimer = INVALID_HANDLE;
    }
    g_bReadySystemActive = false;
}

stock void Ready_Toggle(int client)
{
    if (!g_bReadySystemActive || !IsValidClient(client))
        return;

    if (!g_bMatchLoaded)
        return;

    // Check if player is allowed to ready based on ready_setting
    eReadySettings setting = ReadySettingStringToEnum(g_MatchData.options.ready_setting);
    if (setting == ReadySetting_Admin && !IsPlayerPrivileged(client))
    {
        MessageClient(client, "Only an admin can start the match.");
        return;
    }

    g_bReadyPlayers[client] = !g_bReadyPlayers[client];

    // Update clan tag
    if (g_bReadyPlayers[client])
    {
        CS_SetClientClanTag(client, "[ready]");
        MessageClient(client, "You are now ready.");
    }
    else
    {
        CS_SetClientClanTag(client, "[not ready]");
        MessageClient(client, "You are now not ready.");
    }

    // Check if all required players are ready
    Ready_CheckThreshold();
}

stock void Ready_UnreadyPlayer(int client)
{
    if (client > 0 && client <= MAXPLAYERS)
    {
        g_bReadyPlayers[client] = false;
        if (IsValidClient(client))
        {
            CS_SetClientClanTag(client, "[not ready]");
        }
    }
}

stock void Ready_Skip()
{
    if (!g_bReadySystemActive) return;

    LogMessage("[5Stack] Force starting match (ready skipped)");
    MessageAll("Admin has force-started the match.");
    Ready_TransitionFromWarmup();
}

stock void Ready_CheckThreshold()
{
    if (!g_bReadySystemActive || !g_bMatchLoaded)
        return;

    eReadySettings setting = ReadySettingStringToEnum(g_MatchData.options.ready_setting);
    int required = 0;
    int readyCount = 0;

    switch (setting)
    {
        case ReadySetting_Admin:
        {
            required = 1;
            for (int i = 1; i <= MaxClients; i++)
            {
                if (IsValidClient(i) && g_bReadyPlayers[i] && IsPlayerPrivileged(i))
                    readyCount++;
            }
        }
        case ReadySetting_Captains:
        {
            required = 2;
            if (g_iCaptainCT > 0 && g_bReadyPlayers[g_iCaptainCT]) readyCount++;
            if (g_iCaptainT > 0 && g_bReadyPlayers[g_iCaptainT]) readyCount++;
        }
        case ReadySetting_Players:
        {
            required = GetExpectedPlayerCount();
            for (int i = 1; i <= MaxClients; i++)
            {
                if (IsValidClient(i) && g_bReadyPlayers[i])
                    readyCount++;
            }
        }
    }

    if (readyCount >= required && required > 0)
    {
        Ready_TransitionFromWarmup();
    }
}

stock void Ready_TransitionFromWarmup()
{
    Ready_Reset();
    MatchManager_UpdateMapStatus(MapStatus_Knife);
}

public Action Timer_ReadyStatus(Handle timer)
{
    if (!g_bReadySystemActive)
    {
        g_hReadyTimer = INVALID_HANDLE;
        return Plugin_Stop;
    }

    if (!g_bMatchLoaded) return Plugin_Continue;

    eReadySettings setting = ReadySettingStringToEnum(g_MatchData.options.ready_setting);

    // Show status to each player
    for (int i = 1; i <= MaxClients; i++)
    {
        if (!IsValidClient(i)) continue;

        if (setting == ReadySetting_Admin)
        {
            PrintCenterText(i, "Waiting for admin to start the match");
        }
        else if (setting == ReadySetting_Captains)
        {
            PrintCenterText(i, "Waiting for captains to ready up\nType .ready in chat");
        }
        else
        {
            int readyCount = 0;
            int required = GetExpectedPlayerCount();
            for (int j = 1; j <= MaxClients; j++)
            {
                if (IsValidClient(j) && g_bReadyPlayers[j])
                    readyCount++;
            }
            PrintCenterText(i, "Players ready: %d/%d\nType .ready in chat", readyCount, required);
        }
    }

    // Set clan tags for players who haven't readied
    for (int i = 1; i <= MaxClients; i++)
    {
        if (!IsValidClient(i)) continue;
        if (GetClientTeam(i) <= CS_TEAM_SPECTATOR) continue;

        if (g_bReadyPlayers[i])
        {
            CS_SetClientClanTag(i, "[ready]");
        }
        else
        {
            CS_SetClientClanTag(i, "[not ready]");
        }
    }

    return Plugin_Continue;
}
