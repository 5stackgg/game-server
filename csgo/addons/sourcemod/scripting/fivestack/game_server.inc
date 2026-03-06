/**
 * 5Stack CS:GO Plugin - Game Server
 * Server commands, messaging, and ping.
 */

#if defined _fivestack_game_server_included
    #endinput
#endif
#define _fivestack_game_server_included

stock void SendCommands(const char[] commands)
{
    ServerCommand(commands);
}

stock void MessageAll(const char[] format, any ...)
{
    char buffer[512];
    VFormat(buffer, sizeof(buffer), format, 2);
    PrintToChatAll("[5Stack] %s", buffer);
}

stock void MessageClient(int client, const char[] format, any ...)
{
    char buffer[512];
    VFormat(buffer, sizeof(buffer), format, 3);

    if (IsValidClient(client))
    {
        PrintToChat(client, "[5Stack] %s", buffer);
    }
}

stock void AlertAll(const char[] format, any ...)
{
    char buffer[256];
    VFormat(buffer, sizeof(buffer), format, 2);
    PrintCenterTextAll(buffer);
}

stock void AlertClient(int client, const char[] format, any ...)
{
    char buffer[256];
    VFormat(buffer, sizeof(buffer), format, 3);

    if (IsValidClient(client))
    {
        PrintCenterText(client, buffer);
    }
}

stock void HudAlertAll(const char[] format, any ...)
{
    char buffer[256];
    VFormat(buffer, sizeof(buffer), format, 2);
    PrintHintTextToAll(buffer);
}

stock bool IsValidClient(int client)
{
    return (client > 0 && client <= MaxClients && IsClientInGame(client) && !IsFakeClient(client));
}

// Ping the backend every 15 seconds
stock void Ping_Start()
{
    if (g_hPingTimer != INVALID_HANDLE)
    {
        KillTimer(g_hPingTimer);
    }
    g_hPingTimer = CreateTimer(15.0, Timer_Ping, _, TIMER_REPEAT);
}

public Action Timer_Ping(Handle timer)
{
    if (!Config_IsValid())
        return Plugin_Continue;

    char mapName[MAX_MAP_NAME_LENGTH];
    GetCurrentMap(mapName, sizeof(mapName));

    char endpoint[512];
    FormatEx(endpoint, sizeof(endpoint),
        "game-server-node/ping/%s?map=%s&pluginVersion=%s",
        g_szServerId, mapName, FIVESTACK_VERSION);

    HTTPClient client = CreateApiClient();
    client.Get(endpoint, OnPingResponse);

    return Plugin_Continue;
}

public void OnPingResponse(HTTPResponse response, any value)
{
    if (response.Status != HTTPStatus_OK)
    {
        LogError("[5Stack] Ping failed: HTTP %d", response.Status);
    }
}
