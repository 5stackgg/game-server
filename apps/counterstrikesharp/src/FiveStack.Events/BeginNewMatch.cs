using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnBeginNewMatch(EventBeginNewMatch @event, GameEventInfo info)
    {
        _matchService.GetCurrentMatch()?.WatchPlayerNames();

        return HookResult.Continue;
    }
}
