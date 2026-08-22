using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace UtilityPractice;

public partial class UtilityPracticePlugin
{
    // Practising smokes through your own flash is nobody's idea of practice.
    // Joining a team is the moment somebody is actually in the server and
    // able to read chat -- connect is too early, and a practice server whose
    // commands nobody knows about is a practice server nobody can use.
    [GameEventHandler(HookMode.Post)]
    public HookResult OnPlayerJoinTeam(EventPlayerTeam @event)
    {
        IPlayer? player = @event.UserIdPlayer;

        if (player == null || !player.IsValid || player.IsFakeClient)
        {
            return HookResult.Continue;
        }

        ulong steamId = player.SteamID;

        // Once per connection, not once per team change: switching sides to
        // line something up should not re-print the menu every time.
        if (!_welcomed.Add(steamId))
        {
            return HookResult.Continue;
        }

        Core.Scheduler.DelayBySeconds(
            WelcomeDelaySeconds,
            () =>
            {
                foreach (string line in WelcomeLines)
                {
                    Tell(steamId, line);
                }
            }
        );

        return HookResult.Continue;
    }

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
