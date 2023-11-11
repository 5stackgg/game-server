using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using PlayCS.enums;

namespace PlayCs;

public partial class PlayCsPlugin
{
    private void CapturePlayerDisconnected()
    {
        RegisterEventHandler<EventPlayerDisconnect>(
            (@event, info) =>
            {
                if (@event.Userid == null || @event.Userid.IsBot)
                {
                    return HookResult.Continue;
                }

                if (_currentPhase == ePhase.Warmup || _currentPhase == ePhase.Knife)
                {
                    CsTeam team = TeamNumToCSTeam(@event.Userid.TeamNum);

                    Captains[team] = null;
                }

                return HookResult.Continue;
            }
        );
    }
}
