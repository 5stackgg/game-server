using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;

namespace PlayCs;

public partial class PlayCsPlugin
{
    public void BlockServerCommands()
    {
        AddCommandListener("meta", CommandListener_BlockOutput);
        AddCommandListener("css", CommandListener_BlockOutput);
        AddCommandListener("css_plugins", CommandListener_BlockOutput);
    }
    
    public HookResult CommandListener_BlockOutput(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return HookResult.Continue;
        }

        return HookResult.Stop;
    }
}