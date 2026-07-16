/**
 * 5Stack CS:GO Plugin - Player Chat Events
 * say/say_team hooks -> "chat" event + gag enforcement
 */

#if defined _fivestack_event_chat_included
    #endinput
#endif
#define _fivestack_event_chat_included

stock Action HandleClientChat(int client, const char[] command, const char[] args)
{
    if (!IsValidClient(client) || !g_bMatchLoaded)
        return Plugin_Continue;

    // Check if gagged
    MatchMember member;
    if (GetClientMember(client, member) && member.is_gagged)
    {
        MessageClient(client, "You are gagged in this match.");
        return Plugin_Stop;
    }

    // Don't publish empty messages or command triggers
    if (args[0] == '\0' || args[0] == '.' || args[0] == '!')
        return Plugin_Continue;

    // Spectators can't talk to players
    if (GetClientTeam(client) <= CS_TEAM_SPECTATOR)
    {
        // Still publish the event but block the chat
        PublishChatEvent(client, args);
        return Plugin_Stop;
    }

    PublishChatEvent(client, args);
    return Plugin_Continue;
}

stock void PublishChatEvent(int client, const char[] message)
{
    if (!IsLive() && !IsWarmup() && !IsKnife())
        return;

    char steamId[MAX_STEAMID_LENGTH];
    GetClientAuthId(client, AuthId_SteamID64, steamId, sizeof(steamId));

    // Escape quotes in message for JSON
    char escapedMsg[512];
    int j = 0;
    for (int i = 0; message[i] != '\0' && j < sizeof(escapedMsg) - 2; i++)
    {
        if (message[i] == '"' || message[i] == '\\')
        {
            escapedMsg[j++] = '\\';
        }
        escapedMsg[j++] = message[i];
    }
    escapedMsg[j] = '\0';

    char data[768];
    FormatEx(data, sizeof(data),
        "{\"player\":\"%s\",\"message\":\"%s\"}",
        steamId, escapedMsg);

    PublishGameEvent("chat", data);
}
