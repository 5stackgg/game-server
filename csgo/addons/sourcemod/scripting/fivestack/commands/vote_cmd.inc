/**
 * 5Stack CS:GO Plugin - Vote Commands
 * .y, .n
 */

#if defined _fivestack_cmd_vote_included
    #endinput
#endif
#define _fivestack_cmd_vote_included

public Action Command_VoteYes(int client, int args)
{
    if (client == 0) return Plugin_Handled;
    Vote_Cast(client, true);
    return Plugin_Handled;
}

public Action Command_VoteNo(int client, int args)
{
    if (client == 0) return Plugin_Handled;
    Vote_Cast(client, false);
    return Plugin_Handled;
}
