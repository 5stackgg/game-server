using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using PlayCS.enums;

namespace PlayCs;

public partial class PlayCsPlugin
{
    [GameEventHandler]
    public HookResult OnPlayerDisconnect(EventPlayerConnect @event, GameEventInfo info)
    {
        if (@event.Userid == null || !@event.Userid.IsValid || @event.Userid.IsBot)
        {
            return HookResult.Continue;
        }

        if (_currentPhase == ePhase.Warmup || _currentPhase == ePhase.Knife)
        {
            CsTeam team = TeamNumToCSTeam(@event.Userid.TeamNum);

            _captains[team] = null;
        }

        return HookResult.Continue;
    }
}
