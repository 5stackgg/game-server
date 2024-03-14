using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnDemoStart(EventDemoStart @event, GameEventInfo info)
    {
        Message(HudDestination.Alert, "Demo Started");
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnDemoEnd(EventDemoStop @event, GameEventInfo info)
    {
        Message(HudDestination.Alert, "Demo Finished");
        return HookResult.Continue;
    }
}
