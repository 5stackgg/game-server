/**
 * 5Stack CS:GO Plugin - Help Command
 * .help, .rules
 */

#if defined _fivestack_cmd_help_included
    #endinput
#endif
#define _fivestack_cmd_help_included

public Action Command_Help(int client, int args)
{
    if (client == 0)
    {
        PrintToServer("[5Stack] Available commands:");
        PrintToServer("  .ready / .r     - Toggle ready status");
        PrintToServer("  .stay / .switch - Knife round side selection");
        PrintToServer("  .t / .ct        - Pick side after knife");
        PrintToServer("  .captain        - Claim captain");
        PrintToServer("  .pause / .tech  - Request tech pause");
        PrintToServer("  .resume         - Request resume");
        PrintToServer("  .timeout / .tac - Call tactical timeout");
        PrintToServer("  .y / .n         - Vote yes/no");
        PrintToServer("  .help           - Show this help");
        return Plugin_Handled;
    }

    MessageClient(client, "--- 5Stack Commands ---");
    MessageClient(client, ".ready (.r) - Toggle ready status");
    MessageClient(client, ".stay / .switch - Knife round side selection");
    MessageClient(client, ".t / .ct - Pick side after knife");
    MessageClient(client, ".captain - Claim captain for your team");
    MessageClient(client, ".captains - Show current captains");
    MessageClient(client, ".pause (.tech) - Request technical pause");
    MessageClient(client, ".resume - Request match resume");
    MessageClient(client, ".timeout (.tac) - Call tactical timeout");
    MessageClient(client, ".y / .n - Vote yes/no");
    MessageClient(client, ".help - Show this help");

    return Plugin_Handled;
}
