using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler(HookMode.Post)]
    public HookResult OnBeginNewMatch(EventBeginNewMatch @event)
    {
        _matchService.GetCurrentMatch()?.WatchPlayerNames();

        return HookResult.Continue;
    }
}
