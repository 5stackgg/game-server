using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using FiveStack.enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnGameEnd(EventCsWinPanelMatch @event, GameEventInfo info)
    {
        UpdateMapStatus(eMapStatus.Finished);

        SendCommands(new[] { "tv_stoprecord" });

        return HookResult.Continue;
    }
}
