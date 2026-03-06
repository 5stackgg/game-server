/**
 * 5Stack CS:GO Plugin - Global State
 * Replaces DI singletons from the CS2 plugin.
 */

#if defined _fivestack_globals_included
    #endinput
#endif
#define _fivestack_globals_included

// Plugin version (replaced at build time)
#define FIVESTACK_VERSION "0.1.0"

// Match state
MatchData g_MatchData;
bool g_bMatchLoaded = false;
eMapStatus g_CurrentMapStatus = MapStatus_Unknown;

// Current map cache (index into g_MatchData.match_maps)
int g_iCurrentMapIndex = -1;

// Ready system
bool g_bReadyPlayers[MAXPLAYERS + 1];
Handle g_hReadyTimer = INVALID_HANDLE;
bool g_bReadySystemActive = false;

// Knife system
int g_iKnifeWinnerTeam = CS_TEAM_NONE;
Handle g_hKnifeTimer = INVALID_HANDLE;
bool g_bKnifeRoundActive = false;

// Captain system
int g_iCaptainCT = -1;    // client index or -1
int g_iCaptainT = -1;     // client index or -1

// Timeout system
Handle g_hResumeMessageTimer = INVALID_HANDLE;

// Vote system
StringMap g_smVotes;                // steamid -> bool
char g_szVoteMessage[256];
int g_iVoteAllowedTeam = CS_TEAM_NONE; // 0 = both teams
bool g_bVoteCaptainOnly = false;
float g_fVoteStartTime = 0.0;
float g_fVoteTimeout = 0.0;
Handle g_hVoteTimer = INVALID_HANDLE;
bool g_bVoteActive = false;
// Demo system
bool g_bRecordingDemo = false;
char g_szCurrentDemoPath[PLATFORM_MAX_PATH];

// Event queue
ArrayList g_alPendingEvents;        // ArrayList of JSON strings
Handle g_hEventRetryTimer = INVALID_HANDLE;

// Ping timer
Handle g_hPingTimer = INVALID_HANDLE;

// Map change
Handle g_hMapChangeTimer = INVALID_HANDLE;
Handle g_hMapChangeCountdownTimer = INVALID_HANDLE;
int g_iMapChangeDelay = 0;

// Auth
StringMap g_smAllowedPlayers;       // steamid -> role string (for RCON pre-auth)

stock void Globals_Init()
{
    g_alPendingEvents = new ArrayList(ByteCountToCells(16384));
    g_smAllowedPlayers = new StringMap();
    g_smVotes = new StringMap();
}

stock void Globals_Reset()
{
    g_bMatchLoaded = false;
    g_CurrentMapStatus = MapStatus_Unknown;
    g_iCurrentMapIndex = -1;

    // Ready
    for (int i = 0; i <= MAXPLAYERS; i++)
        g_bReadyPlayers[i] = false;
    if (g_hReadyTimer != INVALID_HANDLE)
    {
        KillTimer(g_hReadyTimer);
        g_hReadyTimer = INVALID_HANDLE;
    }
    g_bReadySystemActive = false;

    // Knife
    g_iKnifeWinnerTeam = CS_TEAM_NONE;
    if (g_hKnifeTimer != INVALID_HANDLE)
    {
        KillTimer(g_hKnifeTimer);
        g_hKnifeTimer = INVALID_HANDLE;
    }
    g_bKnifeRoundActive = false;

    // Captain
    g_iCaptainCT = -1;
    g_iCaptainT = -1;

    // Timeout
    if (g_hResumeMessageTimer != INVALID_HANDLE)
    {
        KillTimer(g_hResumeMessageTimer);
        g_hResumeMessageTimer = INVALID_HANDLE;
    }

    // Vote
    Vote_Cancel();

    // Demo
    g_bRecordingDemo = false;
    g_szCurrentDemoPath[0] = '\0';

    // Map change
    if (g_hMapChangeTimer != INVALID_HANDLE)
    {
        KillTimer(g_hMapChangeTimer);
        g_hMapChangeTimer = INVALID_HANDLE;
    }
    if (g_hMapChangeCountdownTimer != INVALID_HANDLE)
    {
        KillTimer(g_hMapChangeCountdownTimer);
        g_hMapChangeCountdownTimer = INVALID_HANDLE;
    }
    g_iMapChangeDelay = 0;
}

stock int GetCurrentMapIndex()
{
    if (!g_bMatchLoaded) return -1;

    for (int i = 0; i < g_MatchData.match_map_count; i++)
    {
        if (StrEqual(g_MatchData.match_maps[i].id, g_MatchData.current_match_map_id))
        {
            return i;
        }
    }
    return -1;
}

stock bool IsMapFinished()
{
    return (g_CurrentMapStatus == MapStatus_Finished ||
            g_CurrentMapStatus == MapStatus_UploadingDemo ||
            g_CurrentMapStatus == MapStatus_Surrendered);
}

stock bool IsWarmup()
{
    if (g_CurrentMapStatus == MapStatus_Warmup || g_CurrentMapStatus == MapStatus_Scheduled)
        return true;

    return GameRules_GetProp("m_bWarmupPeriod") != 0;
}

stock bool IsLive()
{
    if (IsWarmup() || IsKnife())
        return false;

    return (g_CurrentMapStatus == MapStatus_Live ||
            g_CurrentMapStatus == MapStatus_Overtime ||
            g_CurrentMapStatus == MapStatus_Paused);
}

stock bool IsPaused()
{
    return (g_CurrentMapStatus == MapStatus_Paused);
}

stock bool IsKnife()
{
    return (g_CurrentMapStatus == MapStatus_Knife);
}

stock bool IsOvertime()
{
    return GameRules_GetProp("m_nOvertimePlaying") > 0;
}

stock bool IsFreezePeriod()
{
    return GameRules_GetProp("m_bFreezePeriod") != 0;
}

stock int GetTotalRoundsPlayed()
{
    return GameRules_GetProp("m_totalRoundsPlayed");
}

stock int GetCurrentRound()
{
    return GetTotalRoundsPlayed() + 1;
}

stock int GetExpectedPlayerCount()
{
    if (!g_bMatchLoaded) return 10;

    if (StrEqual(g_MatchData.options.type, "Wingman")) return 4;
    if (StrEqual(g_MatchData.options.type, "Duel")) return 2;
    return 10;
}
