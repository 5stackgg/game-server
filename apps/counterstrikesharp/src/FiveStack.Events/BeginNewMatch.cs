using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnBeginNewMatch(EventBeginNewMatch @event, GameEventInfo info)
    {
        // CS2 reverts renamed players to their Steam name as the match begins (knife / live)
        Server.NextFrame(() => _matchService.GetCurrentMatch()?.RestorePlayerNames());

        return HookResult.Continue;
    }
}
