/**
 * 5Stack CS:GO Plugin - Match Utility
 * Player lookup, member access, damage helpers.
 */

#if defined _fivestack_match_utility_included
    #endinput
#endif
#define _fivestack_match_utility_included

// Find a MatchMember by steam ID or placeholder name.
// Returns true if found, fills out member struct.
stock bool GetMemberFromLineup(const char[] steamId, const char[] playerName, MatchMember member)
{
    if (!g_bMatchLoaded)
        return false;

    // Search lineup 1
    for (int i = 0; i < g_MatchData.lineup_1.player_count; i++)
    {
        if (g_MatchData.lineup_1.players[i].steam_id[0] != '\0')
        {
            if (StrEqual(g_MatchData.lineup_1.players[i].steam_id, steamId))
            {
                member = g_MatchData.lineup_1.players[i];
                return true;
            }
        }
        else if (g_MatchData.lineup_1.players[i].placeholder_name[0] != '\0')
        {
            if (strncmp(g_MatchData.lineup_1.players[i].placeholder_name, playerName,
                strlen(g_MatchData.lineup_1.players[i].placeholder_name)) == 0)
            {
                member = g_MatchData.lineup_1.players[i];
                return true;
            }
        }
    }

    // Search lineup 2
    for (int i = 0; i < g_MatchData.lineup_2.player_count; i++)
    {
        if (g_MatchData.lineup_2.players[i].steam_id[0] != '\0')
        {
            if (StrEqual(g_MatchData.lineup_2.players[i].steam_id, steamId))
            {
                member = g_MatchData.lineup_2.players[i];
                return true;
            }
        }
        else if (g_MatchData.lineup_2.players[i].placeholder_name[0] != '\0')
        {
            if (strncmp(g_MatchData.lineup_2.players[i].placeholder_name, playerName,
                strlen(g_MatchData.lineup_2.players[i].placeholder_name)) == 0)
            {
                member = g_MatchData.lineup_2.players[i];
                return true;
            }
        }
    }

    return false;
}

// Get the MatchMember for a connected client
stock bool GetClientMember(int client, MatchMember member)
{
    if (!IsValidClient(client))
        return false;

    char steamId[MAX_STEAMID_LENGTH];
    GetClientAuthId(client, AuthId_SteamID64, steamId, sizeof(steamId));

    char playerName[MAX_NAME_LENGTH];
    GetClientName(client, playerName, sizeof(playerName));

    return GetMemberFromLineup(steamId, playerName, member);
}

// Check if player is an admin/organizer role
stock bool IsPlayerPrivileged(int client)
{
    MatchMember member;
    if (!GetClientMember(client, member))
    {
        // Check RCON-allowed players
        char steamId[MAX_STEAMID_LENGTH];
        GetClientAuthId(client, AuthId_SteamID64, steamId, sizeof(steamId));

        char role[MAX_ROLE_LENGTH];
        if (g_smAllowedPlayers.GetString(steamId, role, sizeof(role)))
        {
            ePlayerRoles playerRole = PlayerRoleStringToEnum(role);
            return (playerRole == PlayerRole_Administrator ||
                    playerRole == PlayerRole_MatchOrganizer ||
                    playerRole == PlayerRole_TournamentOrganizer);
        }
        return false;
    }

    ePlayerRoles playerRole = PlayerRoleStringToEnum(member.role);
    return (playerRole == PlayerRole_Administrator ||
            playerRole == PlayerRole_MatchOrganizer ||
            playerRole == PlayerRole_TournamentOrganizer);
}

// Get expected team for a player based on lineup assignment
stock int GetExpectedTeam(int client)
{
    if (!g_bMatchLoaded || g_iCurrentMapIndex < 0)
        return CS_TEAM_NONE;

    char lineupId[GUID_LENGTH];
    if (!GetPlayerLineupId(client, lineupId, sizeof(lineupId)))
        return CS_TEAM_SPECTATOR;

    return GetLineupSide(lineupId, GetTotalRoundsPlayed());
}

// Enforce that a player is on their correct team
stock void EnforceMemberTeam(int client)
{
    if (!IsValidClient(client)) return;

    int expectedTeam = GetExpectedTeam(client);
    if (expectedTeam == CS_TEAM_NONE) return;

    int currentTeam = GetClientTeam(client);
    if (currentTeam != expectedTeam)
    {
        char teamStr[16];
        CSTeamToString(expectedTeam, teamStr, sizeof(teamStr));
        LogMessage("[5Stack] Switching %N to %s", client, teamStr);

        CS_SwitchTeam(client, expectedTeam);
        MessageClient(client, "You've been assigned to %s.", teamStr);
    }

    // Apply member properties
    MatchMember member;
    if (GetClientMember(client, member))
    {
        if (member.is_banned)
        {
            KickClient(client, "You are banned from this match");
            return;
        }

        if (member.is_muted)
        {
            SetClientListeningFlags(client, VOICE_MUTED);
        }
        else
        {
            SetClientListeningFlags(client, VOICE_NORMAL);
        }
    }

    // Respawn during warmup or freeze period
    if (IsWarmup() || IsFreezePeriod())
    {
        if (expectedTeam != CS_TEAM_SPECTATOR)
        {
            CS_RespawnPlayer(client);
        }
    }

    // Set clan tag
    char tag[MAX_TAG_LENGTH];
    if (GetPlayerLineupTag(client, tag, sizeof(tag)))
    {
        CS_SetClientClanTag(client, tag);
    }
}

// HitGroup int to string mapping
stock void HitGroupToString(int hitgroup, char[] buffer, int maxlen)
{
    switch (hitgroup)
    {
        case 0: strcopy(buffer, maxlen, "generic");
        case 1: strcopy(buffer, maxlen, "head");
        case 2: strcopy(buffer, maxlen, "chest");
        case 3: strcopy(buffer, maxlen, "stomach");
        case 4: strcopy(buffer, maxlen, "left arm");
        case 5: strcopy(buffer, maxlen, "right arm");
        case 6: strcopy(buffer, maxlen, "left leg");
        case 7: strcopy(buffer, maxlen, "right leg");
        case 8: strcopy(buffer, maxlen, "neck");
        default: strcopy(buffer, maxlen, "unknown");
    }
}
