using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace PlayCs;

public partial class PlayCsPlugin
{
    private void CapturePlayerSwitchedSides()
    {
        RegisterEventHandler<EventPlayerDisconnect>(
            (@event, info) =>
            {
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
