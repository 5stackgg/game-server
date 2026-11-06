/**
 * 5Stack CS:GO Plugin - Team Utility
 * Side calculation with overtime math, score, money helpers.
 */

#if defined _fivestack_team_utility_included
    #endinput
#endif
#define _fivestack_team_utility_included

stock int GetOppositeSide(int side)
{
    if (side == CS_TEAM_T) return CS_TEAM_CT;
    if (side == CS_TEAM_CT) return CS_TEAM_T;
    return CS_TEAM_NONE;
}

stock int TeamStringToCSTeam(const char[] team)
{
    if (StrEqual(team, "TERRORIST")) return CS_TEAM_T;
    if (StrEqual(team, "CT")) return CS_TEAM_CT;
    if (StrEqual(team, "Spectator")) return CS_TEAM_SPECTATOR;
    return CS_TEAM_NONE;
}

stock void CSTeamToString(int team, char[] buffer, int maxlen)
{
    switch (team)
    {
        case CS_TEAM_T:         strcopy(buffer, maxlen, "TERRORIST");
        case CS_TEAM_CT:        strcopy(buffer, maxlen, "CT");
        case CS_TEAM_SPECTATOR: strcopy(buffer, maxlen, "Spectator");
        default:                strcopy(buffer, maxlen, "None");
    }
}

// Determine which CS team a lineup should be on for a given round.
// Handles regular time halves and overtime rotation.
stock int GetLineupSide(const char[] lineupId, int round)
{
    if (!g_bMatchLoaded || g_iCurrentMapIndex < 0)
        return CS_TEAM_NONE;

    int mr = g_MatchData.options.mr;
    if (mr <= 0)
        return CS_TEAM_NONE;

    bool isLineup1 = StrEqual(g_MatchData.lineup_1_id, lineupId);
    char sideStr[MAX_SIDE_LENGTH];
    if (isLineup1)
        strcopy(sideStr, sizeof(sideStr), g_MatchData.match_maps[g_iCurrentMapIndex].lineup_1_side);
    else
        strcopy(sideStr, sizeof(sideStr), g_MatchData.match_maps[g_iCurrentMapIndex].lineup_2_side);

    int startingSide = TeamStringToCSTeam(sideStr);
    if (startingSide == CS_TEAM_NONE || startingSide == CS_TEAM_SPECTATOR)
        return CS_TEAM_NONE;

    // Regular time
    if (round < mr * 2)
    {
        if (round < mr)
            return startingSide;     // First half
        return GetOppositeSide(startingSide); // Second half
    }

    // Overtime
    int overtimeRound = round - (mr * 2);
    ConVar cvOtMaxRounds = FindConVar("mp_overtime_maxrounds");
    int overtimeMr = (cvOtMaxRounds != null) ? cvOtMaxRounds.IntValue : 6;

    int overTimeNumber = (overtimeRound / overtimeMr) + 1;
    int block = overtimeRound % overtimeMr;

    bool condition = (overTimeNumber % 2 == 1) ? (block < (overtimeMr / 2)) : (block >= (overtimeMr / 2));
    return condition ? GetOppositeSide(startingSide) : startingSide;
}

// Get team score for a lineup at the current round
stock int GetLineupScore(const char[] lineupId)
{
    int round = GetTotalRoundsPlayed();
    int expectedSide = GetLineupSide(lineupId, round);
    if (expectedSide == CS_TEAM_NONE)
        return 0;

    return GetTeamScore(expectedSide);
}

// Get team money sum for a given CS team
stock int GetTeamMoney(int csTeam)
{
    int totalCash = 0;
    for (int i = 1; i <= MaxClients; i++)
    {
        if (!IsValidClient(i)) continue;
        if (GetClientTeam(i) != csTeam) continue;

        totalCash += GetEntProp(i, Prop_Send, "m_iAccount");
    }
    return totalCash;
}

// Get the lineup ID for a given player
stock bool GetPlayerLineupId(int client, char[] lineupId, int maxlen)
{
    if (!g_bMatchLoaded || !IsValidClient(client))
    {
        lineupId[0] = '\0';
        return false;
    }

    char steamId[MAX_STEAMID_LENGTH];
    GetClientAuthId(client, AuthId_SteamID64, steamId, sizeof(steamId));

    char playerName[MAX_NAME_LENGTH];
    GetClientName(client, playerName, sizeof(playerName));

    // Search lineup 1
    for (int i = 0; i < g_MatchData.lineup_1.player_count; i++)
    {
        if (g_MatchData.lineup_1.players[i].steam_id[0] != '\0')
        {
            if (StrEqual(g_MatchData.lineup_1.players[i].steam_id, steamId))
            {
                strcopy(lineupId, maxlen, g_MatchData.lineup_1_id);
                return true;
            }
        }
        else if (g_MatchData.lineup_1.players[i].placeholder_name[0] != '\0')
        {
            if (strncmp(g_MatchData.lineup_1.players[i].placeholder_name, playerName,
                strlen(g_MatchData.lineup_1.players[i].placeholder_name)) == 0)
            {
                strcopy(lineupId, maxlen, g_MatchData.lineup_1_id);
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
                strcopy(lineupId, maxlen, g_MatchData.lineup_2_id);
                return true;
            }
        }
        else if (g_MatchData.lineup_2.players[i].placeholder_name[0] != '\0')
        {
            if (strncmp(g_MatchData.lineup_2.players[i].placeholder_name, playerName,
                strlen(g_MatchData.lineup_2.players[i].placeholder_name)) == 0)
            {
                strcopy(lineupId, maxlen, g_MatchData.lineup_2_id);
                return true;
            }
        }
    }

    lineupId[0] = '\0';
    return false;
}

// Get lineup tag string (e.g. "[TAG]") for a player
stock bool GetPlayerLineupTag(int client, char[] tag, int maxlen)
{
    char lineupId[GUID_LENGTH];
    if (!GetPlayerLineupId(client, lineupId, sizeof(lineupId)))
    {
        tag[0] = '\0';
        return false;
    }

    if (StrEqual(g_MatchData.lineup_1_id, lineupId) || StrEqual(g_MatchData.lineup_1.id, lineupId))
    {
        if (g_MatchData.lineup_1.tag[0] != '\0')
        {
            FormatEx(tag, maxlen, "[%s]", g_MatchData.lineup_1.tag);
            return true;
        }
    }
    else
    {
        if (g_MatchData.lineup_2.tag[0] != '\0')
        {
            FormatEx(tag, maxlen, "[%s]", g_MatchData.lineup_2.tag);
            return true;
        }
    }

    tag[0] = '\0';
    return false;
}

// Count human players on a team
stock int GetTeamPlayerCount(int csTeam)
{
    int count = 0;
    for (int i = 1; i <= MaxClients; i++)
    {
        if (IsValidClient(i) && GetClientTeam(i) == csTeam)
            count++;
    }
    return count;
}
