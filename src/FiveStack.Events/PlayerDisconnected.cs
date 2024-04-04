using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        MatchManager? match = CurrentMatch();
        MatchMap? currentMap = match?.GetCurrentMap();

        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || @event.Userid.IsBot
            || match == null
            || currentMap == null
        )
        {
            return HookResult.Continue;
        }

        if (match.IsWarmup() || match.IsKnife())
        {
            match.captainSystem.RemoveCaptain(@event.Userid);
        }

        if (match.IsLive())
        {
            _gameServer.SendCommands(new[] { "mp_pause_match" });
            _gameServer.Message(HudDestination.Center, $" {ChatColors.Red}Match Paused");
        }

        return HookResult.Continue;
    }
}
