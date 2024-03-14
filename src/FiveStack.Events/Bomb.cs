using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult BombPlaned(EventBombPlanted @event, GameEventInfo info)
    {
        if (@event.Userid == null || !@event.Userid.IsValid || _matchData == null || !IsLive())
        {
            return HookResult.Continue;
        }

        CCSPlayerController attacked = @event.Userid;

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult BombPlaned(EventBombDefused @event, GameEventInfo info)
    {
        if (@event.Userid == null || !@event.Userid.IsValid || _matchData == null || !IsLive())
        {
            return HookResult.Continue;
        }

        CCSPlayerController attacked = @event.Userid;

        return HookResult.Continue;
    }
}
