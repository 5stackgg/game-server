/**
 * 5Stack CS:GO Plugin - Player Utility Events
 * *_detonate -> "utility" events, player_blind -> "flash" event
 */

#if defined _fivestack_event_utility_included
    #endinput
#endif
#define _fivestack_event_utility_included

public Action Event_DecoyDetonate(Event event, const char[] name, bool dontBroadcast)
{
    return PublishUtilityEvent(event, "Decoy");
}

public Action Event_HEGrenadeDetonate(Event event, const char[] name, bool dontBroadcast)
{
    return PublishUtilityEvent(event, "HighExplosive");
}

public Action Event_FlashbangDetonate(Event event, const char[] name, bool dontBroadcast)
{
    return PublishUtilityEvent(event, "Flash");
}

public Action Event_MolotovDetonate(Event event, const char[] name, bool dontBroadcast)
{
    return PublishUtilityEvent(event, "Molotov");
}

public Action Event_SmokeDetonate(Event event, const char[] name, bool dontBroadcast)
{
    return PublishUtilityEvent(event, "Smoke");
}

stock Action PublishUtilityEvent(Event event, const char[] utilityType)
{
    if (!IsLive()) return Plugin_Continue;

    int client = GetClientOfUserId(event.GetInt("userid"));
    if (!IsValidClient(client)) return Plugin_Continue;

    char steamId[MAX_STEAMID_LENGTH];
    GetClientAuthId(client, AuthId_SteamID64, steamId, sizeof(steamId));

    float x = event.GetFloat("x");
    float y = event.GetFloat("y");
    float z = event.GetFloat("z");

    char data[512];
    FormatEx(data, sizeof(data),
        "{\"attacker_steam_id\":\"%s\",\"attacker_team\":%d,"
        "\"type\":\"%s\","
        "\"attacker_location_coordinates\":{\"x\":%.2f,\"y\":%.2f,\"z\":%.2f},"
        "\"round\":%d}",
        steamId, GetClientTeam(client),
        utilityType,
        x, y, z,
        GetCurrentRound());

    PublishGameEvent("utility", data);
    return Plugin_Continue;
}

public Action Event_PlayerBlind(Event event, const char[] name, bool dontBroadcast)
{
    if (!IsLive()) return Plugin_Continue;

    int victim = GetClientOfUserId(event.GetInt("userid"));
    int attacker = GetClientOfUserId(event.GetInt("attacker"));

    if (!IsValidClient(victim) || !IsValidClient(attacker))
        return Plugin_Continue;

    char victimSteamId[MAX_STEAMID_LENGTH];
    GetClientAuthId(victim, AuthId_SteamID64, victimSteamId, sizeof(victimSteamId));

    char attackerSteamId[MAX_STEAMID_LENGTH];
    GetClientAuthId(attacker, AuthId_SteamID64, attackerSteamId, sizeof(attackerSteamId));

    float duration = event.GetFloat("blind_duration");
    bool teamFlash = (GetClientTeam(victim) == GetClientTeam(attacker));

    char data[512];
    FormatEx(data, sizeof(data),
        "{\"attacker_steam_id\":\"%s\",\"attacker_team\":%d,"
        "\"attacked_steam_id\":\"%s\",\"attacked_team\":%d,"
        "\"duration\":%.2f,\"team_flash\":%s,"
        "\"round\":%d}",
        attackerSteamId, GetClientTeam(attacker),
        victimSteamId, GetClientTeam(victim),
        duration, teamFlash ? "true" : "false",
        GetCurrentRound());

    PublishGameEvent("flash", data);
    return Plugin_Continue;
}
