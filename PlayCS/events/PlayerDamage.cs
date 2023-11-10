using CounterStrikeSharp.API.Core;

namespace PlayCs;

public partial class PlayCsPlugin
{
    public void CapturePlayerDamage()
    {
        RegisterEventHandler<EventPlayerHurt>(
            (@event, info) =>
            {
                if (matchId == null)
                {
                    return HookResult.Continue;
                }

                CCSPlayerController attacker = @event.Attacker;

                CCSPlayerController attacked = @event.Userid;

                // Prepare the data using the generic class
                Redis.EventData<Dictionary<string, object>> eventData = new Redis.EventData<
                    Dictionary<string, object>
                >
                {
                    @event = "damage",
                    data = new Dictionary<string, object>
                    {
                        { "attacker_steam_id", attacker.SteamID },
                        { "attacker_team", $"{TeamNumToString(attacker.TeamNum)}" },
                        { "weapon", $"{@event.Weapon}" },
                        { "damage", $"{@event.DmgHealth}" },
                        { "damage_armor", $"{@event.DmgArmor}" },
                        { "hitgroup", $"{HitGroupToString(@event.Hitgroup)}" },
                        { "attcked_steam_id", attacked.SteamID },
                        { "attacked_team", $"{TeamNumToString(attacked.TeamNum)}" },
                    }
                };

                redis.Publish(matchId, eventData);

                return HookResult.Continue;
            }
        );
    }
}
