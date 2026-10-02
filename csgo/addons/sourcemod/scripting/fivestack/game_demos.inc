/**
 * 5Stack CS:GO Plugin - Game Demos
 * tv_record, presigned URL upload, completion notify.
 */

#if defined _fivestack_game_demos_included
    #endinput
#endif
#define _fivestack_game_demos_included

stock void Demo_Start()
{
    if (g_bRecordingDemo || !g_bMatchLoaded || g_iCurrentMapIndex < 0)
        return;

    char mapName[MAX_MAP_NAME_LENGTH];
    GetCurrentMap(mapName, sizeof(mapName));

    int timestamp = GetTime();

    FormatEx(g_szCurrentDemoPath, sizeof(g_szCurrentDemoPath),
        "%s_%s_%d-%s",
        g_MatchData.id,
        g_MatchData.match_maps[g_iCurrentMapIndex].id,
        timestamp, mapName);

    char cmd[PLATFORM_MAX_PATH];
    FormatEx(cmd, sizeof(cmd), "tv_record \"%s\"", g_szCurrentDemoPath);
    SendCommands(cmd);

    g_bRecordingDemo = true;
    LogMessage("[5Stack] Demo recording started: %s", g_szCurrentDemoPath);
}

stock void Demo_Stop()
{
    if (!g_bRecordingDemo) return;

    SendCommands("tv_stoprecord");
    g_bRecordingDemo = false;
    LogMessage("[5Stack] Demo recording stopped");
}

stock void Demo_Upload()
{
    if (!g_bMatchLoaded || !Config_IsValid())
        return;

    if (g_szCurrentDemoPath[0] == '\0')
    {
        LogMessage("[5Stack] No demo to upload");
        return;
    }

    LogMessage("[5Stack] Requesting presigned URL for demo upload");

    // Request presigned URL
    char endpoint[512];
    FormatEx(endpoint, sizeof(endpoint),
        "demos/%s/pre-signed?demo=%s.dem&mapId=%s",
        g_MatchData.id, g_szCurrentDemoPath,
        g_MatchData.match_maps[g_iCurrentMapIndex].id);

    HTTPClient client = CreateDemosClient();
    client.Get(endpoint, OnDemoPresignedUrl);
    // Note: client handle leaks, but ripext internally manages the lifecycle
    // for active requests. The handle is cleaned up after the callback fires.
}

public void OnDemoPresignedUrl(HTTPResponse response, any value)
{
    int statusCode = view_as<int>(response.Status);

    if (statusCode == 409)
    {
        LogMessage("[5Stack] Demo upload: map not finished yet (409)");
        return;
    }

    if (statusCode == 406)
    {
        LogMessage("[5Stack] Demo upload: already uploaded (406)");
        return;
    }

    if (statusCode != 200)
    {
        LogError("[5Stack] Demo presigned URL failed: HTTP %d", statusCode);
        return;
    }

    JSONObject json = view_as<JSONObject>(response.Data);
    if (json == null) return;

    char presignedUrl[1024];
    json.GetString("presignedUrl", presignedUrl, sizeof(presignedUrl));

    if (presignedUrl[0] == '\0')
    {
        LogError("[5Stack] Empty presigned URL");
        return;
    }

    // Upload the demo file via PUT to the presigned URL using HTTPRequest
    char demoFile[PLATFORM_MAX_PATH];
    FormatEx(demoFile, sizeof(demoFile), "%s.dem", g_szCurrentDemoPath);

    LogMessage("[5Stack] Uploading demo: %s to presigned URL", demoFile);

    // Use ripext HTTPRequest for PUT
    HTTPRequest request = new HTTPRequest(presignedUrl);
    request.UploadFile("", demoFile, OnDemoUploaded);
}

public void OnDemoUploaded(HTTPResponse response, any value)
{
    int statusCode = view_as<int>(response.Status);

    if (statusCode != 200 && statusCode != 201 && statusCode != 204)
    {
        LogError("[5Stack] Demo upload failed: HTTP %d", statusCode);
        return;
    }

    LogMessage("[5Stack] Demo uploaded successfully, notifying backend");

    // Notify backend that upload is complete
    if (!g_bMatchLoaded || g_iCurrentMapIndex < 0) return;

    char endpoint[256];
    FormatEx(endpoint, sizeof(endpoint), "demos/%s/uploaded", g_MatchData.id);

    char demoFile[PLATFORM_MAX_PATH];
    FormatEx(demoFile, sizeof(demoFile), "%s.dem", g_szCurrentDemoPath);

    JSONObject body = new JSONObject();
    body.SetString("demo", demoFile);
    body.SetString("mapId", g_MatchData.match_maps[g_iCurrentMapIndex].id);

    HTTPClient client = CreateDemosClient();
    client.Post(endpoint, body, OnDemoNotified);
    delete body;
}

public void OnDemoNotified(HTTPResponse response, any value)
{
    int statusCode = view_as<int>(response.Status);

    if (statusCode != 200)
    {
        LogError("[5Stack] Demo upload notification failed: HTTP %d", statusCode);
        return;
    }

    LogMessage("[5Stack] Backend notified of demo upload");
}
