/**
 * 5Stack CS:GO Plugin - Bomb Events
 * bomb_planted/defused/exploded -> "objective"
 */

#if defined _fivestack_event_bomb_included
    #endinput
#endif
#define _fivestack_event_bomb_included

public Action Event_BombPlanted(Event event, const char[] name, bool dontBroadcast)
{
    if (!IsLive()) return Plugin_Continue;

    int client = GetClientOfUserId(event.GetInt("userid"));
    if (!IsValidClient(client)) return Plugin_Continue;

    char steamId[MAX_STEAMID_LENGTH];
    GetClientAuthId(client, AuthId_SteamID64, steamId, sizeof(steamId));

    int site = event.GetInt("site");

    char data[256];
    FormatEx(data, sizeof(data),
        "{\"type\":\"Planted\",\"player_steam_id\":\"%s\",\"site\":%d,\"round\":%d}",
        steamId, site, GetCurrentRound());

    PublishGameEvent("objective", data);
    return Plugin_Continue;
}

public Action Event_BombDefused(Event event, const char[] name, bool dontBroadcast)
{
    if (!IsLive()) return Plugin_Continue;

    int client = GetClientOfUserId(event.GetInt("userid"));
    if (!IsValidClient(client)) return Plugin_Continue;

    char steamId[MAX_STEAMID_LENGTH];
    GetClientAuthId(client, AuthId_SteamID64, steamId, sizeof(steamId));

    int site = event.GetInt("site");

    char data[256];
    FormatEx(data, sizeof(data),
        "{\"type\":\"Defused\",\"player_steam_id\":\"%s\",\"site\":%d,\"round\":%d}",
        steamId, site, GetCurrentRound());

    PublishGameEvent("objective", data);
    return Plugin_Continue;
}

public Action Event_BombExploded(Event event, const char[] name, bool dontBroadcast)
{
    if (!IsLive()) return Plugin_Continue;

    int site = event.GetInt("site");

    char data[256];
    FormatEx(data, sizeof(data),
        "{\"type\":\"Exploded\",\"player_steam_id\":\"\",\"site\":%d,\"round\":%d}",
        site, GetCurrentRound());

    PublishGameEvent("objective", data);
    return Plugin_Continue;
}
