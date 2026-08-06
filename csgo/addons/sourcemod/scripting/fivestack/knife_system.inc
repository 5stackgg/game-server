/**
 * 5Stack CS:GO Plugin - Knife System
 * Knife round, winner tracking, stay/switch.
 */

#if defined _fivestack_knife_system_included
    #endinput
#endif
#define _fivestack_knife_system_included

stock void Knife_Start()
{
    if (IsKnife()) return;

    LogMessage("[5Stack] Starting knife round");

    g_bKnifeRoundActive = true;
    g_iKnifeWinnerTeam = CS_TEAM_NONE;

    SendCommands("exec 5stack.knife.cfg");
    SendCommands("mp_warmup_end; mp_restartgame 1");

    MessageAll("Knife round! Winner picks side.");
}

stock void Knife_SetWinner(int winnerTeam)
{
    g_iKnifeWinnerTeam = winnerTeam;

    char teamStr[16];
    CSTeamToString(winnerTeam, teamStr, sizeof(teamStr));
    LogMessage("[5Stack] Knife round won by %s", teamStr);

    // Start the captain prompt
    if (g_hKnifeTimer != INVALID_HANDLE)
    {
        KillTimer(g_hKnifeTimer);
    }
    g_hKnifeTimer = CreateTimer(3.0, Timer_KnifePrompt, _, TIMER_REPEAT);

    // Enter warmup for decision
    SendCommands("mp_warmup_start");
}

public Action Timer_KnifePrompt(Handle timer)
{
    if (g_iKnifeWinnerTeam == CS_TEAM_NONE)
    {
        g_hKnifeTimer = INVALID_HANDLE;
        return Plugin_Stop;
    }

    // Find the captain of the winning team
    int captain = (g_iKnifeWinnerTeam == CS_TEAM_CT) ? g_iCaptainCT : g_iCaptainT;

    if (captain > 0 && IsValidClient(captain))
    {
        PrintCenterText(captain, "Your team won the knife round!\nType .stay or .switch to pick a side\nOr type .ct / .t");
    }

    // Show waiting message to everyone else
    for (int i = 1; i <= MaxClients; i++)
    {
        if (!IsValidClient(i) || i == captain) continue;
        PrintCenterText(i, "Waiting for captain to pick a side...");
    }

    return Plugin_Continue;
}

stock void Knife_Stay(int client)
{
    if (g_iKnifeWinnerTeam == CS_TEAM_NONE)
    {
        MessageClient(client, "No knife round decision pending.");
        return;
    }

    if (!Knife_IsCaptainOfWinner(client))
    {
        MessageClient(client, "Only the captain of the winning team can decide.");
        return;
    }

    LogMessage("[5Stack] Captain chose to STAY");
    MessageAll("Captain has chosen to stay on the current side.");

    Knife_Finalize(false);
}

stock void Knife_Switch(int client)
{
    if (g_iKnifeWinnerTeam == CS_TEAM_NONE)
    {
        MessageClient(client, "No knife round decision pending.");
        return;
    }

    if (!Knife_IsCaptainOfWinner(client))
    {
        MessageClient(client, "Only the captain of the winning team can decide.");
        return;
    }

    LogMessage("[5Stack] Captain chose to SWITCH");
    MessageAll("Captain has chosen to switch sides.");

    Knife_Finalize(true);
}

stock void Knife_PickSide(int client, int side)
{
    if (g_iKnifeWinnerTeam == CS_TEAM_NONE)
    {
        MessageClient(client, "No knife round decision pending.");
        return;
    }

    if (!Knife_IsCaptainOfWinner(client))
    {
        MessageClient(client, "Only the captain of the winning team can decide.");
        return;
    }

    int currentSide = GetClientTeam(client);
    bool shouldSwitch = (currentSide != side);

    if (shouldSwitch)
    {
        LogMessage("[5Stack] Captain picked %s (switch)", side == CS_TEAM_CT ? "CT" : "T");
        MessageAll("Captain has chosen to switch sides.");
    }
    else
    {
        LogMessage("[5Stack] Captain picked %s (stay)", side == CS_TEAM_CT ? "CT" : "T");
        MessageAll("Captain has chosen to stay on the current side.");
    }

    Knife_Finalize(shouldSwitch);
}

stock void Knife_Skip()
{
    LogMessage("[5Stack] Knife round skipped");
    MessageAll("Knife round has been skipped.");

    g_iKnifeWinnerTeam = CS_TEAM_NONE;
    g_bKnifeRoundActive = false;

    if (g_hKnifeTimer != INVALID_HANDLE)
    {
        KillTimer(g_hKnifeTimer);
        g_hKnifeTimer = INVALID_HANDLE;
    }

    MatchManager_UpdateMapStatus(MapStatus_Live);
}

stock void Knife_Finalize(bool doSwitch)
{
    if (g_hKnifeTimer != INVALID_HANDLE)
    {
        KillTimer(g_hKnifeTimer);
        g_hKnifeTimer = INVALID_HANDLE;
    }

    if (doSwitch)
    {
        SendCommands("mp_swapteams");
        PublishGameEvent("switch", "{}");
    }

    g_iKnifeWinnerTeam = CS_TEAM_NONE;
    g_bKnifeRoundActive = false;

    MatchManager_UpdateMapStatus(MapStatus_Live);
}

stock bool Knife_IsCaptainOfWinner(int client)
{
    if (g_iKnifeWinnerTeam == CS_TEAM_NONE) return false;

    int captain = (g_iKnifeWinnerTeam == CS_TEAM_CT) ? g_iCaptainCT : g_iCaptainT;
    return (client == captain);
}

stock void Knife_Reset()
{
    g_iKnifeWinnerTeam = CS_TEAM_NONE;
    g_bKnifeRoundActive = false;

    if (g_hKnifeTimer != INVALID_HANDLE)
    {
        KillTimer(g_hKnifeTimer);
        g_hKnifeTimer = INVALID_HANDLE;
    }
}
