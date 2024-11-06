using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundPrestart @event, GameEventInfo info)
    {
        int currentPlayers = MatchUtility.Players().Count;

        int expectedPlayers = _matchService.GetCurrentMatch()?.GetExpectedPlayerCount() ?? 10;

        if (expectedPlayers != currentPlayers)
        {
            _timeoutSystem.PauseMatch("Waiting for players to reconnect");
        }

        _logger.LogInformation("Round started");
        return HookResult.Continue;
    }
}
