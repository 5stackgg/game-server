using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using Microsoft.Extensions.Logging;
using PlayCS.enums;

namespace PlayCs;

public partial class PlayCsPlugin
{
    [GameEventHandler]
    public HookResult OnGameEnd(EventGameEnd @event, GameEventInfo info)
    {
        UpdateGameState(eGameState.Finished);

        SendCommands(new[] { "tv_stoprecord" });

        return HookResult.Continue;
    }
}
