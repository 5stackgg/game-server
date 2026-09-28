using FiveStack.Utilities;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler(HookMode.Post)]
    public HookResult OnBeginNewMatch(EventBeginNewMatch @event)
    {
        // CS2 reverts renamed players to their Steam name as the match begins (knife / live)
        MatchUtility.Core.Scheduler.NextTick(
            () => _matchService.GetCurrentMatch()?.RestorePlayerNames()
        );

        return HookResult.Continue;
    }
}
