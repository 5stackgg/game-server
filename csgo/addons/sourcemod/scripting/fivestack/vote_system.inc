/**
 * 5Stack CS:GO Plugin - Vote System
 * Generic vote framework: team-scoped, timed, callback on pass/fail.
 */

#if defined _fivestack_vote_system_included
    #endinput
#endif
#define _fivestack_vote_system_included

Function g_fnVoteSuccess = INVALID_FUNCTION;
Function g_fnVoteFail = INVALID_FUNCTION;

stock void Vote_Start(const char[] message, int allowedTeam, Function successCb, Function failCb,
                      bool captainOnly = false, float timeout = 30.0)
{
    Vote_Cancel(); // Cancel any existing vote

    strcopy(g_szVoteMessage, sizeof(g_szVoteMessage), message);
    g_iVoteAllowedTeam = allowedTeam;
    g_bVoteCaptainOnly = captainOnly;
    g_fVoteStartTime = GetGameTime();
    g_fVoteTimeout = timeout;
    g_bVoteActive = true;
    g_fnVoteSuccess = successCb;
    g_fnVoteFail = failCb;

    if (g_smVotes != null)
        g_smVotes.Clear();
    else
        g_smVotes = new StringMap();

    g_hVoteTimer = CreateTimer(1.0, Timer_VoteDisplay, _, TIMER_REPEAT);

    // Announce
    for (int i = 1; i <= MaxClients; i++)
    {
        if (!IsValidClient(i)) continue;
        int team = GetClientTeam(i);
        if (g_iVoteAllowedTeam != CS_TEAM_NONE && team != g_iVoteAllowedTeam) continue;

        MessageClient(i, "Vote: %s — Type .y or .n", message);
    }
}

stock void Vote_Cast(int client, bool vote)
{
    if (!g_bVoteActive || !IsValidClient(client))
        return;

    int team = GetClientTeam(client);
    if (g_iVoteAllowedTeam != CS_TEAM_NONE && team != g_iVoteAllowedTeam)
    {
        MessageClient(client, "You are not eligible to vote.");
        return;
    }

    if (g_bVoteCaptainOnly && !Captain_IsCaptain(client, team))
    {
        MessageClient(client, "Only captains can vote.");
        return;
    }

    char steamId[MAX_STEAMID_LENGTH];
    GetClientAuthId(client, AuthId_SteamID64, steamId, sizeof(steamId));

    g_smVotes.SetValue(steamId, vote);

    MessageClient(client, "You voted %s.", vote ? "yes" : "no");

    Vote_CheckResult();
}

stock void Vote_Cancel()
{
    g_bVoteActive = false;
    g_fnVoteSuccess = INVALID_FUNCTION;
    g_fnVoteFail = INVALID_FUNCTION;

    if (g_hVoteTimer != INVALID_HANDLE)
    {
        KillTimer(g_hVoteTimer);
        g_hVoteTimer = INVALID_HANDLE;
    }

    if (g_smVotes != null)
        g_smVotes.Clear();
}

stock void Vote_CheckResult()
{
    if (!g_bVoteActive) return;

    int yesVotes = 0;
    int noVotes = 0;
    int totalVotes = 0;
    int eligibleCount = 0;

    // Count eligible voters
    for (int i = 1; i <= MaxClients; i++)
    {
        if (!IsValidClient(i)) continue;
        int team = GetClientTeam(i);
        if (g_iVoteAllowedTeam != CS_TEAM_NONE && team != g_iVoteAllowedTeam) continue;
        if (g_bVoteCaptainOnly && !Captain_IsCaptain(i, team)) continue;
        eligibleCount++;
    }

    // Count votes
    StringMapSnapshot snap = g_smVotes.Snapshot();
    totalVotes = snap.Length;

    for (int i = 0; i < snap.Length; i++)
    {
        char key[MAX_STEAMID_LENGTH];
        snap.GetKey(i, key, sizeof(key));

        bool vote;
        g_smVotes.GetValue(key, vote);
        if (vote) yesVotes++;
        else noVotes++;
    }
    delete snap;

    // Check if captain-only vote
    if (g_bVoteCaptainOnly)
    {
        if (totalVotes >= 2)
        {
            if (yesVotes >= 2)
                Vote_Pass();
            else
                Vote_Fail();
        }
        return;
    }

    // Standard vote: need majority
    int required = (eligibleCount / 2) + 1;
    if (yesVotes >= required)
    {
        Vote_Pass();
    }
    else if (noVotes > eligibleCount - required)
    {
        Vote_Fail();
    }
    else if (totalVotes >= eligibleCount)
    {
        // Everyone voted
        if (yesVotes >= required)
            Vote_Pass();
        else
            Vote_Fail();
    }
}

stock void Vote_Pass()
{
    if (!g_bVoteActive) return;

    MessageAll("Vote passed: %s", g_szVoteMessage);

    Function cb = g_fnVoteSuccess;
    Vote_Cancel();

    if (cb != INVALID_FUNCTION)
    {
        Call_StartFunction(null, cb);
        Call_Finish();
    }
}

stock void Vote_Fail()
{
    if (!g_bVoteActive) return;

    MessageAll("Vote failed: %s", g_szVoteMessage);

    Function cb = g_fnVoteFail;
    Vote_Cancel();

    if (cb != INVALID_FUNCTION)
    {
        Call_StartFunction(null, cb);
        Call_Finish();
    }
}

public Action Timer_VoteDisplay(Handle timer)
{
    if (!g_bVoteActive)
    {
        g_hVoteTimer = INVALID_HANDLE;
        return Plugin_Stop;
    }

    // Check timeout
    float elapsed = GetGameTime() - g_fVoteStartTime;
    if (elapsed >= g_fVoteTimeout)
    {
        Vote_Fail();
        return Plugin_Stop;
    }

    int remaining = RoundToCeil(g_fVoteTimeout - elapsed);

    for (int i = 1; i <= MaxClients; i++)
    {
        if (!IsValidClient(i)) continue;

        int team = GetClientTeam(i);
        bool eligible = (g_iVoteAllowedTeam == CS_TEAM_NONE || team == g_iVoteAllowedTeam);

        if (!eligible)
        {
            PrintCenterText(i, "Other team voting: %s (%ds remaining)", g_szVoteMessage, remaining);
            continue;
        }

        char steamId[MAX_STEAMID_LENGTH];
        GetClientAuthId(i, AuthId_SteamID64, steamId, sizeof(steamId));

        bool hasVoted;
        if (g_smVotes.GetValue(steamId, hasVoted))
        {
            PrintCenterText(i, "You voted on: %s (%ds remaining)", g_szVoteMessage, remaining);
        }
        else
        {
            PrintCenterText(i, "Vote: %s\nType .y or .n (%ds remaining)", g_szVoteMessage, remaining);
        }
    }

    return Plugin_Continue;
}
