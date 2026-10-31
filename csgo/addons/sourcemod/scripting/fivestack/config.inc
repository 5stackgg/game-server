/**
 * 5Stack CS:GO Plugin - Configuration
 * Loads SERVER_ID, API_PASSWORD, domain URLs from cfg file.
 */

#if defined _fivestack_config_included
    #endinput
#endif
#define _fivestack_config_included

#define MAX_URL_LENGTH 256

// Config globals
char g_szServerId[GUID_LENGTH];
char g_szServerApiPassword[MAX_PASSWORD_LENGTH];
char g_szApiDomain[MAX_URL_LENGTH];
char g_szDemosDomain[MAX_URL_LENGTH];

// ConVars
ConVar g_cvServerId;
ConVar g_cvServerApiPassword;
ConVar g_cvApiDomain;
ConVar g_cvDemosDomain;

stock void Config_Init()
{
    g_cvServerId = CreateConVar("fivestack_server_id", "", "5Stack Server ID", FCVAR_PROTECTED);
    g_cvServerApiPassword = CreateConVar("fivestack_server_api_password", "", "5Stack Server API Password", FCVAR_PROTECTED);
    g_cvApiDomain = CreateConVar("fivestack_api_domain", "https://api.5stack.gg", "5Stack API Domain");
    g_cvDemosDomain = CreateConVar("fivestack_demos_domain", "https://demos.5stack.gg", "5Stack Demos Domain");

    AutoExecConfig(true, "fivestack", "fivestack");
}

stock void Config_Load()
{
    g_cvServerId.GetString(g_szServerId, sizeof(g_szServerId));
    g_cvServerApiPassword.GetString(g_szServerApiPassword, sizeof(g_szServerApiPassword));
    g_cvApiDomain.GetString(g_szApiDomain, sizeof(g_szApiDomain));
    g_cvDemosDomain.GetString(g_szDemosDomain, sizeof(g_szDemosDomain));

    // Strip trailing slashes
    int len;
    len = strlen(g_szApiDomain);
    if (len > 0 && g_szApiDomain[len - 1] == '/') g_szApiDomain[len - 1] = '\0';

    len = strlen(g_szDemosDomain);
    if (len > 0 && g_szDemosDomain[len - 1] == '/') g_szDemosDomain[len - 1] = '\0';
}

stock bool Config_IsValid()
{
    return (g_szServerId[0] != '\0' && g_szServerApiPassword[0] != '\0');
}
