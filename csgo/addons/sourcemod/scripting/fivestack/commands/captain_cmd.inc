/**
 * 5Stack CS:GO Plugin - Captain Commands
 * .captain, .captains, .release-captain
 */

#if defined _fivestack_cmd_captain_included
    #endinput
#endif
#define _fivestack_cmd_captain_included

public Action Command_Captain(int client, int args)
{
    if (client == 0) return Plugin_Handled;
    Captain_Claim(client);
    return Plugin_Handled;
}

public Action Command_Captains(int client, int args)
{
    Captain_ShowCaptains();
    return Plugin_Handled;
}

public Action Command_ReleaseCaptain(int client, int args)
{
    if (client == 0) return Plugin_Handled;
    Captain_Release(client);
    return Plugin_Handled;
}
