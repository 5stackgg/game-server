/**
 * 5Stack CS:GO Plugin - Captain System
 * Captain claim, auto-select, release.
 */

#if defined _fivestack_captain_system_included
    #endinput
#endif
#define _fivestack_captain_system_included

stock void Captain_Reset()
{
    g_iCaptainCT = -1;
    g_iCaptainT = -1;
}

stock void Captain_AutoSelect()
{
    if (!g_bMatchLoaded) return;

    // Try to find marked captains from lineup data
    for (int client = 1; client <= MaxClients; client++)
    {
        if (!IsValidClient(client)) continue;

        int team = GetClientTeam(client);
        if (team != CS_TEAM_CT && team != CS_TEAM_T) continue;

        MatchMember member;
        if (GetClientMember(client, member) && member.captain)
        {
            if (team == CS_TEAM_CT && g_iCaptainCT < 0)
            {
                g_iCaptainCT = client;
                LogMessage("[5Stack] Auto-selected CT captain: %N", client);
            }
            else if (team == CS_TEAM_T && g_iCaptainT < 0)
            {
                g_iCaptainT = client;
                LogMessage("[5Stack] Auto-selected T captain: %N", client);
            }
        }
    }

    // If no captain was found for a team, pick the first player
    if (g_iCaptainCT < 0)
    {
        for (int client = 1; client <= MaxClients; client++)
        {
            if (IsValidClient(client) && GetClientTeam(client) == CS_TEAM_CT)
            {
                g_iCaptainCT = client;
                LogMessage("[5Stack] Auto-assigned CT captain: %N", client);
                break;
            }
        }
    }

    if (g_iCaptainT < 0)
    {
        for (int client = 1; client <= MaxClients; client++)
        {
            if (IsValidClient(client) && GetClientTeam(client) == CS_TEAM_T)
            {
                g_iCaptainT = client;
                LogMessage("[5Stack] Auto-assigned T captain: %N", client);
                break;
            }
        }
    }
}

stock void Captain_Claim(int client)
{
    if (!IsValidClient(client)) return;

    int team = GetClientTeam(client);
    if (team != CS_TEAM_CT && team != CS_TEAM_T)
    {
        MessageClient(client, "You must be on a team to claim captain.");
        return;
    }

    if (team == CS_TEAM_CT)
    {
        g_iCaptainCT = client;
    }
    else
    {
        g_iCaptainT = client;
    }

    MessageAll("%N is now captain of %s.", client, team == CS_TEAM_CT ? "CT" : "T");

    // Publish captain event
    char steamId[MAX_STEAMID_LENGTH];
    GetClientAuthId(client, AuthId_SteamID64, steamId, sizeof(steamId));

    char data[256];
    FormatEx(data, sizeof(data),
        "{\"steam_id\":\"%s\",\"claim\":true}", steamId);
    PublishGameEvent("captain", data);
}

stock void Captain_Release(int client)
{
    if (!IsValidClient(client)) return;

    int team = GetClientTeam(client);
    bool wasCaptain = false;

    if (team == CS_TEAM_CT && g_iCaptainCT == client)
    {
        g_iCaptainCT = -1;
        wasCaptain = true;
    }
    else if (team == CS_TEAM_T && g_iCaptainT == client)
    {
        g_iCaptainT = -1;
        wasCaptain = true;
    }

    if (wasCaptain)
    {
        MessageAll("%N has released captain.", client);

        char steamId[MAX_STEAMID_LENGTH];
        GetClientAuthId(client, AuthId_SteamID64, steamId, sizeof(steamId));

        char data[256];
        FormatEx(data, sizeof(data),
            "{\"steam_id\":\"%s\",\"claim\":false}", steamId);
        PublishGameEvent("captain", data);
    }
    else
    {
        MessageClient(client, "You are not a captain.");
    }
}

stock void Captain_ShowCaptains()
{
    char ctName[MAX_NAME_LENGTH] = "None";
    char tName[MAX_NAME_LENGTH] = "None";

    if (g_iCaptainCT > 0 && IsValidClient(g_iCaptainCT))
        GetClientName(g_iCaptainCT, ctName, sizeof(ctName));

    if (g_iCaptainT > 0 && IsValidClient(g_iCaptainT))
        GetClientName(g_iCaptainT, tName, sizeof(tName));

    MessageAll("CT Captain: %s | T Captain: %s", ctName, tName);
}

stock bool Captain_IsCaptain(int client, int team)
{
    if (team == CS_TEAM_CT) return (client == g_iCaptainCT);
    if (team == CS_TEAM_T) return (client == g_iCaptainT);
    return false;
}

stock void Captain_RemoveDisconnected(int client)
{
    if (g_iCaptainCT == client) g_iCaptainCT = -1;
    if (g_iCaptainT == client) g_iCaptainT = -1;
}
