using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace PlayCs;

public partial class PlayCsPlugin
{
    private void CapturePlayerDisconnected()
    {
        RegisterEventHandler<EventPlayerDisconnect>(
            (@event, info) =>
            {
                if (@event.Userid.IsBot)
                {
                    return HookResult.Continue;
                }

                if (CurrentPhase == ePhase.Warmup || CurrentPhase == ePhase.Knife)
                {
                    CsTeam team = TeamNumToCSTeam(@event.Userid.TeamNum);

                    Captains[team] = null;
                }

                return HookResult.Continue;
            }
        );
    }
}
