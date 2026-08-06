/**
 * 5Stack CS:GO Plugin - Match Service
 * Fetches match data from the API and parses it into global state.
 */

#if defined _fivestack_match_service_included
    #endinput
#endif
#define _fivestack_match_service_included

stock void MatchService_FetchMatch()
{
    if (!Config_IsValid())
    {
        LogError("[5Stack] Cannot fetch match: Server ID or API password not configured");
        return;
    }

    char endpoint[256];
    FormatEx(endpoint, sizeof(endpoint), "matches/current-match/%s", g_szServerId);

    HTTPClient client = CreateApiClient();
    client.Get(endpoint, OnMatchFetched);
}

public void OnMatchFetched(HTTPResponse response, any value)
{
    if (response.Status != HTTPStatus_OK)
    {
        LogError("[5Stack] Failed to fetch match data: HTTP %d", response.Status);
        return;
    }

    JSONObject json = view_as<JSONObject>(response.Data);
    if (json == null)
    {
        LogError("[5Stack] Match response was not valid JSON");
        return;
    }

    // Parse match data into globals
    if (!JSON_ParseMatchData(json, g_MatchData))
    {
        LogError("[5Stack] Failed to parse match data");
        return;
    }

    g_bMatchLoaded = true;
    g_iCurrentMapIndex = GetCurrentMapIndex();

    LogMessage("[5Stack] Match loaded: %s (map index: %d)", g_MatchData.id, g_iCurrentMapIndex);

    // Trigger match setup
    MatchManager_SetupMatch();
}
