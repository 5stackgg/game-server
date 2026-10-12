/**
 * 5Stack CS:GO Plugin - HTTP Client
 * HTTP wrappers using sm-ripext for API communication.
 */

#if defined _fivestack_http_client_included
    #endinput
#endif
#define _fivestack_http_client_included

#include <ripext>

// Create an HTTPClient with auth headers pointing to the API domain
stock HTTPClient CreateApiClient()
{
    HTTPClient client = new HTTPClient(g_szApiDomain);
    char authHeader[256];
    FormatEx(authHeader, sizeof(authHeader), "Bearer %s", g_szServerApiPassword);
    client.SetHeader("Authorization", authHeader);
    client.SetHeader("Content-Type", "application/json");
    return client;
}

// Create an HTTPClient for the demos domain
stock HTTPClient CreateDemosClient()
{
    HTTPClient client = new HTTPClient(g_szDemosDomain);
    char authHeader[256];
    FormatEx(authHeader, sizeof(authHeader), "Bearer %s", g_szServerApiPassword);
    client.SetHeader("Authorization", authHeader);
    client.SetHeader("Content-Type", "application/json");
    return client;
}
