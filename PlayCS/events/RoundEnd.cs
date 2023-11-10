using CounterStrikeSharp.API.Core;

namespace PlayCs;

public partial class PlayCsPlugin
{
    private void CaptureRoundEnd()
    {
        RegisterEventHandler<EventRoundEnd>(
            (@event, info) =>
            {
                return HookResult.Continue;
            }
        );
    }
}
