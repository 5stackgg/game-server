using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _logger.LogInformation($"RoundStart: {_gameServer.GetCurrentRound()}");
        MatchManager? matchManager = _matchService.GetCurrentMatch();
        if (matchManager == null)
        {
            return HookResult.Continue;
        }

        if (!matchManager.IsLive())
        {
            return HookResult.Continue;
        }

        PublishRoundInformation(true);

        int currentPlayers = MatchUtility.Players().Count;

        int expectedPlayers = _matchService.GetCurrentMatch()?.GetExpectedPlayerCount() ?? 10;

        if (currentPlayers < expectedPlayers)
        {
            matchManager.PauseMatch("Waiting for players to reconnect");
        }

        return HookResult.Continue;
    }
}
