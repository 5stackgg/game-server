/**
 * 5Stack CS:GO Plugin - Demo Commands
 * upload_demos, test_start_demo, test_stop_demo (server commands)
 */

#if defined _fivestack_cmd_demo_included
    #endinput
#endif
#define _fivestack_cmd_demo_included

public Action Command_UploadDemos(int args)
{
    LogMessage("[5Stack] Manual demo upload triggered");
    Demo_Upload();
    return Plugin_Handled;
}

public Action Command_TestStartDemo(int args)
{
    LogMessage("[5Stack] Test demo recording started");
    Demo_Start();
    return Plugin_Handled;
}

public Action Command_TestStopDemo(int args)
{
    LogMessage("[5Stack] Test demo recording stopped");
    Demo_Stop();
    return Plugin_Handled;
}
