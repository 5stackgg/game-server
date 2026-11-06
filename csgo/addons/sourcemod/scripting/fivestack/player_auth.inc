/**
 * 5Stack CS:GO Plugin - Player Authentication
 * OnClientConnect lineup check, RCON sm_fivestack_allow pre-auth.
 */

#if defined _fivestack_player_auth_included
    #endinput
#endif
#define _fivestack_player_auth_included

public bool OnClientConnect(int client, char[] rejectmsg, int maxlen)
{
    if (IsFakeClient(client))
        return true;

    if (!g_bMatchLoaded)
        return true;  // Allow connection if no match loaded yet

    // Get steam ID — may not be available yet at this point
    // We'll do the full check in OnClientAuthorized instead
    return true;
}

public void OnClientAuthorized(int client, const char[] auth)
{
    if (IsFakeClient(client))
        return;

    if (!g_bMatchLoaded)
        return;

    char steamId[MAX_STEAMID_LENGTH];
    GetClientAuthId(client, AuthId_SteamID64, steamId, sizeof(steamId));

    // Check if pre-authorized via RCON
    char role[MAX_ROLE_LENGTH];
    if (g_smAllowedPlayers.GetString(steamId, role, sizeof(role)))
    {
        LogMessage("[5Stack] Pre-authorized player connected: %s (role: %s)", steamId, role);
        return;
    }

    // Check if player is in either lineup
    char playerName[MAX_NAME_LENGTH];
    GetClientName(client, playerName, sizeof(playerName));

    MatchMember member;
    if (GetMemberFromLineup(steamId, playerName, member))
    {
        LogMessage("[5Stack] Lineup player connected: %s (%s)", steamId, member.name);
        return;
    }

    // Player not in lineup and not pre-authorized
    LogMessage("[5Stack] Rejecting player %s — not in match lineup", steamId);
    KickClient(client, "You are not part of this match. Visit 5stack.gg to join.");
}

// RCON command to pre-authorize a player
public Action Command_FiveStackAllow(int args)
{
    if (args < 2)
    {
        PrintToServer("Usage: sm_fivestack_allow <steamid64> <role>");
        return Plugin_Handled;
    }

    char steamId[MAX_STEAMID_LENGTH];
    char role[MAX_ROLE_LENGTH];

    GetCmdArg(1, steamId, sizeof(steamId));
    GetCmdArg(2, role, sizeof(role));

    g_smAllowedPlayers.SetString(steamId, role);
    PrintToServer("[5Stack] Pre-authorized player %s with role %s", steamId, role);
    return Plugin_Handled;
}
