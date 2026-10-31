/**
 * 5Stack CS:GO Plugin - Player Kill Events
 * player_death -> "kill" + "assist" events
 */

#if defined _fivestack_event_kills_included
    #endinput
#endif
#define _fivestack_event_kills_included

public Action Event_PlayerDeath(Event event, const char[] name, bool dontBroadcast)
{
    if (!IsLive()) return Plugin_Continue;

    int victim = GetClientOfUserId(event.GetInt("userid"));
    int attacker = GetClientOfUserId(event.GetInt("attacker"));
    int assister = GetClientOfUserId(event.GetInt("assister"));

    if (!IsValidClient(victim)) return Plugin_Continue;

    // Get victim info
    char victimSteamId[MAX_STEAMID_LENGTH];
    GetClientAuthId(victim, AuthId_SteamID64, victimSteamId, sizeof(victimSteamId));

    float victimPos[3];
    GetClientAbsOrigin(victim, victimPos);

    char victimPlace[64];
    GetEntPropString(victim, Prop_Send, "m_szLastPlaceName", victimPlace, sizeof(victimPlace));

    // Get attacker info
    char attackerSteamId[MAX_STEAMID_LENGTH] = "";
    float attackerPos[3] = {0.0, 0.0, 0.0};
    char attackerPlace[64] = "";
    int attackerTeam = CS_TEAM_NONE;

    if (IsValidClient(attacker))
    {
        GetClientAuthId(attacker, AuthId_SteamID64, attackerSteamId, sizeof(attackerSteamId));
        GetClientAbsOrigin(attacker, attackerPos);
        GetEntPropString(attacker, Prop_Send, "m_szLastPlaceName", attackerPlace, sizeof(attackerPlace));
        attackerTeam = GetClientTeam(attacker);
    }

    // Get weapon and kill details
    char weapon[64];
    event.GetString("weapon", weapon, sizeof(weapon));

    bool headshot = event.GetBool("headshot");
    bool penetrated = event.GetInt("penetrated") > 0;

    // player_death doesn't carry hitgroup — infer from headshot flag
    char hitgroupStr[32];
    if (headshot)
        strcopy(hitgroupStr, sizeof(hitgroupStr), "Head");
    else
        strcopy(hitgroupStr, sizeof(hitgroupStr), "Generic");

    // Build kill event
    char data[2048];
    FormatEx(data, sizeof(data),
        "{\"attacker_steam_id\":\"%s\",\"attacker_team\":%d,"
        "\"attacker_location_coordinates\":{\"x\":%.2f,\"y\":%.2f,\"z\":%.2f},"
        "\"attacker_location\":\"%s\","
        "\"attacked_steam_id\":\"%s\",\"attacked_team\":%d,"
        "\"attacked_location_coordinates\":{\"x\":%.2f,\"y\":%.2f,\"z\":%.2f},"
        "\"attacked_location\":\"%s\","
        "\"weapon\":\"%s\",\"hitgroup\":\"%s\","
        "\"headshot\":%s,\"penetrated\":%s,"
        "\"round\":%d}",
        attackerSteamId, attackerTeam,
        attackerPos[0], attackerPos[1], attackerPos[2],
        attackerPlace,
        victimSteamId, GetClientTeam(victim),
        victimPos[0], victimPos[1], victimPos[2],
        victimPlace,
        weapon, hitgroupStr,
        headshot ? "true" : "false",
        penetrated ? "true" : "false",
        GetCurrentRound());

    PublishGameEvent("kill", data);

    // Handle assist
    if (IsValidClient(assister))
    {
        char assisterSteamId[MAX_STEAMID_LENGTH];
        GetClientAuthId(assister, AuthId_SteamID64, assisterSteamId, sizeof(assisterSteamId));

        char assistData[512];
        FormatEx(assistData, sizeof(assistData),
            "{\"attacker_steam_id\":\"%s\",\"attacker_team\":%d,"
            "\"attacked_steam_id\":\"%s\",\"attacked_team\":%d,"
            "\"round\":%d}",
            assisterSteamId, GetClientTeam(assister),
            victimSteamId, GetClientTeam(victim),
            GetCurrentRound());

        PublishGameEvent("assist", assistData);
    }

    return Plugin_Continue;
}
