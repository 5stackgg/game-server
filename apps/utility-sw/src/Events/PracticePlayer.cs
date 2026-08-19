using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace UtilityPractice;

public partial class UtilityPracticePlugin
{
    // Practising smokes through your own flash is nobody's idea of practice.
    [GameEventHandler(HookMode.Post)]
    public HookResult OnPlayerBlind(EventPlayerBlind @event)
    {
        if (!_config.NoFlash)
        {
            return HookResult.Continue;
        }

        CCSPlayerPawn? pawn = @event.UserIdPawn;

        if (pawn == null || !pawn.IsValid)
        {
            return HookResult.Continue;
        }

        pawn.FlashDuration = 0f;

        return HookResult.Continue;
    }
}
