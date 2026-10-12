/**
 * 5Stack CS:GO Plugin - Ready Commands
 * .ready, .unready, force_ready
 */

#if defined _fivestack_cmd_ready_included
    #endinput
#endif
#define _fivestack_cmd_ready_included

public Action Command_Ready(int client, int args)
{
    if (client == 0) return Plugin_Handled;
    Ready_Toggle(client);
    return Plugin_Handled;
}

public Action Command_ForceReady(int args)
{
    Ready_Skip();
    return Plugin_Handled;
}
