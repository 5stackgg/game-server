using CounterStrikeSharp.API.Core;

namespace PlayCs;

public partial class PlayCsPlugin
{
    private void CaptureRoundEnd()
    {
        RegisterEventHandler<EventRoundEnd>(
            (@event, info) =>
            {
                Console.WriteLine($"ROUND END: {CurrentPhase.ToString()}");
                if (CurrentPhase == ePhase.Knife)
                {
                    Console.WriteLine($"TEAM ASSIGNED {@event.Winner}");

                    KnifeWinningTeam = TeamNumToCSTeam(@event.Winner);

                    NotifyCaptainSideSelection();

                    return HookResult.Continue;
                }

                return HookResult.Continue;
            }
        );
    }
}
