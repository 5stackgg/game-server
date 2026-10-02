/**
 * 5Stack CS:GO Plugin - Player Disconnected Events
 * player_disconnect -> "player-disconnected"
 */

#if defined _fivestack_event_disconnected_included
    #endinput
#endif
#define _fivestack_event_disconnected_included

public Action Event_PlayerDisconnect(Event event, const char[] name, bool dontBroadcast)
{
    int client = GetClientOfUserId(event.GetInt("userid"));
    if (client <= 0 || client > MaxClients) return Plugin_Continue;
    if (IsFakeClient(client)) return Plugin_Continue;

    // Use GetClientAuthId with IsClientConnected check — client may be partially disconnected
    char steamId[MAX_STEAMID_LENGTH];
    if (!IsClientConnected(client) || !GetClientAuthId(client, AuthId_SteamID64, steamId, sizeof(steamId)))
    {
        // Fallback: get networkid from event (SteamID2 format)
        event.GetString("networkid", steamId, sizeof(steamId));
    }

    // Publish disconnect event
    char data[128];
    FormatEx(data, sizeof(data), "{\"steam_id\":\"%s\"}", steamId);
    PublishGameEvent("player-disconnected", data);

    // During warmup/knife: unready and remove captain
    if (IsWarmup() || IsKnife())
    {
        Ready_UnreadyPlayer(client);
        Captain_RemoveDisconnected(client);
    }

    // During live: pause if in freeze period
    if (IsLive() && IsFreezePeriod())
    {
        MatchManager_PauseMatch("Player disconnected — waiting for reconnect");
    }

    return Plugin_Continue;
}
