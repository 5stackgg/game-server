using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using static SwiftlyS2.Shared.Helper;

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
        // Read before anything is done about it: NoFlash zeroes the duration,
        // and a flash you cannot measure is a flash you cannot practise.
        float duration = @event.BlindDuration;

        IPlayer? blinded = @event.UserIdPlayer;
        IPlayer? thrower = @event.AttackerPlayer;

        // Only a bot's blindness is worth reporting. A player who flashed
        // themselves already knows, and one who flashed a team-mate is told by
        // the team-mate.
        if (blinded != null && blinded.IsValid && blinded.IsFakeClient && duration > 0f)
        {
            ReportFlash(thrower, duration);
        }

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

    // What a grenade actually did to a bot, which is the only reason to have
    // put one there. Reported to the thrower, in chat, because it is a number
    // to compare against the last attempt rather than something to read at a
    // glance mid-throw.
    [GameEventHandler(HookMode.Post)]
    public HookResult OnPlayerHurt(EventPlayerHurt @event)
    {
        IPlayer? hurt = @event.UserIdPlayer;
        IPlayer? attacker = @event.AttackerPlayer;

        if (hurt == null || !hurt.IsValid || !hurt.IsFakeClient)
        {
            return HookResult.Continue;
        }

        if (attacker == null || !attacker.IsValid || attacker.IsFakeClient)
        {
            return HookResult.Continue;
        }

        string weapon = @event.Weapon ?? "";

        // Bullets are not what this server is for, and a bot being shot would
        // fill the chat with numbers nobody asked for.
        if (!weapon.Contains("grenade") && !weapon.Contains("molotov") && !weapon.Contains("inferno"))
        {
            return HookResult.Continue;
        }

        // DmgHealth is the raw figure and overkills a bot on low health, which
        // would read as a better grenade than it was.
        int dealt = Math.Min(@event.DmgHealth, @event.Health + @event.DmgHealth);

        Tell(
            attacker.SteamID,
            $" {ChatColors.Default}{dealt} {ChatColors.Grey}damage to a bot "
                + $"{ChatColors.Default}({Friendly(weapon)})"
        );

        return HookResult.Continue;
    }

    private void ReportFlash(IPlayer? thrower, float duration)
    {
        if (thrower == null || !thrower.IsValid || thrower.IsFakeClient)
        {
            return;
        }

        Tell(
            thrower.SteamID,
            $" {ChatColors.Default}{duration:0.00}s {ChatColors.Grey}of flash on a bot"
        );
    }

    private static string Friendly(string weapon)
    {
        return weapon switch
        {
            "hegrenade" => "he",
            "molotov" => "molotov",
            "incgrenade" => "incendiary",
            "inferno" => "fire",
            _ => weapon,
        };
    }
}
