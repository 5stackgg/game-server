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
                    data = new Dictionary<string, object> { { "name", "hello" }, { "damage", 123 } }
                };

                redis.Publish(matchId, eventData);

                return HookResult.Continue;
            }
        );
    }
}
