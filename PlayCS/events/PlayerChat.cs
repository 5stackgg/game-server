using CounterStrikeSharp.API.Core;

namespace PlayCs;

public partial class PlayCsPlugin
{
    private void CapatureChat()
    {
        RegisterEventHandler<EventPlayerChat>(
            (@event, info) =>
            {
                return HookResult.Continue;
            }
        );
    }
}
