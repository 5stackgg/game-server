using CounterStrikeSharp.API.Core;

namespace PlayCs;

public partial class PlayCsPlugin
{
    private void CaptureChat()
    {
        RegisterEventHandler<EventPlayerChat>(
            (@event, info) =>
            {
                return HookResult.Continue;
            }
        );
    }
}
