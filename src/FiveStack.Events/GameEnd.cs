using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using Microsoft.Extensions.Logging;
using FiveStack.enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnGameEnd(EventGameEnd @event, GameEventInfo info)
    {
        UpdateGameState(eGameState.Finished);

        SendCommands(new[] { "tv_stoprecord" });

        return HookResult.Continue;
    }
}
