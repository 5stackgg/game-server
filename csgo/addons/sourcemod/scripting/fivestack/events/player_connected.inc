/**
 * 5Stack CS:GO Plugin - Player Connected Events
 * player_connect_full -> "player-connected", team join handling
 */

#if defined _fivestack_event_connected_included
    #endinput
#endif
#define _fivestack_event_connected_included

public Action Event_PlayerConnectFull(Event event, const char[] name, bool dontBroadcast)
{
    int client = GetClientOfUserId(event.GetInt("userid"));
    if (!IsValidClient(client)) return Plugin_Continue;

    char steamId[MAX_STEAMID_LENGTH];
    GetClientAuthId(client, AuthId_SteamID64, steamId, sizeof(steamId));

    char playerName[MAX_NAME_LENGTH];
    GetClientName(client, playerName, sizeof(playerName));

    // Publish player connected event
    char data[256];
    FormatEx(data, sizeof(data),
        "{\"player_name\":\"%s\",\"steam_id\":\"%s\"}",
        playerName, steamId);
    PublishGameEvent("player-connected", data);

    // Enforce team assignment
    if (g_bMatchLoaded)
    {
        CreateTimer(0.5, Timer_EnforceMemberTeam, GetClientUserId(client));
    }

    // Show hints
    if (g_bReadySystemActive)
    {
        MessageClient(client, "Type .ready in chat when you are ready to play.");
    }

    MessageClient(client, "Type .help for available commands.");

    return Plugin_Continue;
}

public Action Timer_EnforceMemberTeam(Handle timer, any userId)
{
    int client = GetClientOfUserId(userId);
    if (IsValidClient(client))
    {
        EnforceMemberTeam(client);
    }
    return Plugin_Stop;
}
