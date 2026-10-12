/**
 * 5Stack CS:GO Plugin - Knife Commands
 * .stay, .switch, .t, .ct, skip_knife
 */

#if defined _fivestack_cmd_knife_included
    #endinput
#endif
#define _fivestack_cmd_knife_included

public Action Command_Stay(int client, int args)
{
    if (client == 0) return Plugin_Handled;
    Knife_Stay(client);
    return Plugin_Handled;
}

public Action Command_Switch(int client, int args)
{
    if (client == 0) return Plugin_Handled;
    Knife_Switch(client);
    return Plugin_Handled;
}

public Action Command_PickT(int client, int args)
{
    if (client == 0) return Plugin_Handled;
    Knife_PickSide(client, CS_TEAM_T);
    return Plugin_Handled;
}

public Action Command_PickCT(int client, int args)
{
    if (client == 0) return Plugin_Handled;
    Knife_PickSide(client, CS_TEAM_CT);
    return Plugin_Handled;
}

public Action Command_SkipKnife(int args)
{
    Knife_Skip();
    return Plugin_Handled;
}
