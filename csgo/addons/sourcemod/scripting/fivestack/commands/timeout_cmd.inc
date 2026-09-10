/**
 * 5Stack CS:GO Plugin - Timeout Commands
 * .pause, .resume, .timeout
 */

#if defined _fivestack_cmd_timeout_included
    #endinput
#endif
#define _fivestack_cmd_timeout_included

public Action Command_Pause(int client, int args)
{
    if (client == 0) return Plugin_Handled;
    Timeout_RequestPause(client);
    return Plugin_Handled;
}

public Action Command_Resume(int client, int args)
{
    if (client == 0) return Plugin_Handled;
    Timeout_RequestResume(client);
    return Plugin_Handled;
}

public Action Command_TacTimeout(int client, int args)
{
    if (client == 0) return Plugin_Handled;
    Timeout_CallTactical(client);
    return Plugin_Handled;
}
