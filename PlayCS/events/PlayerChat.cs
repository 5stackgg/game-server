using CounterStrikeSharp.API.Core;

namespace PlayCs;

public partial class PlayCsPlugin
{
    public void CapatureChat()
    {
        RegisterEventHandler<EventPlayerChat>(
            (@event, info) =>
            {
                return HookResult.Continue;
            }
        );
    }
}
