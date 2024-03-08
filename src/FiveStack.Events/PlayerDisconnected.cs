using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnPlayerDisconnect(EventPlayerConnect @event, GameEventInfo info)
    {
        if (@event.Userid == null || !@event.Userid.IsValid || @event.Userid.IsBot)
        {
            return HookResult.Continue;
        }

        if (_currentGameState == eGameState.Warmup || _currentGameState == eGameState.Knife)
        {
            CsTeam team = TeamNumToCSTeam(@event.Userid.TeamNum);

            _captains[team] = null;
        }

        if (IsLive())
        {
            SendCommands(new[] { "mp_pause_match" });
            Message(HudDestination.Center, $" {ChatColors.Red}Match Paused");
        }

        return HookResult.Continue;
    }
}
