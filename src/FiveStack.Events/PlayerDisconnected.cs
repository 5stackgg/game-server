using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using FiveStack.Entities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
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

        CCSPlayerController player = @event.Userid;

        _matchEvents.PublishGameEvent(
            "player-disconnected",
            new Dictionary<string, object> { { "steam_id", player.SteamID.ToString() } }
        );

        if (match.IsWarmup() || match.IsKnife())
        {
            match.readySystem.UnreadyPlayer(player);
            match.captainSystem.RemoveCaptain(@event.Userid);
        }

        if (match.IsLive())
        {
            _surrenderSystem.SetupDisconnectTimer(@event.Userid.Team, @event.Userid.SteamID);
        }

        return HookResult.Continue;
    }
}
