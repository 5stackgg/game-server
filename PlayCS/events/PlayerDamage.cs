using CounterStrikeSharp.API.Core;

namespace PlayCs;

public partial class PlayCsPlugin
{
    public void CapturePlayerDamage()
    {
        RegisterEventHandler<EventPlayerHurt>(
            (@event, info) =>
            {
                string matchId = "6556546546";

                CCSPlayerController attacker = @event.Attacker;

                CCSPlayerController attacked = @event.Userid;

                // Prepare the data using the generic class
                Redis.EventData<Dictionary<string, object>> eventData = new Redis.EventData<
                    Dictionary<string, object>
                >
                {
                    @event = "test",
                    data = new Dictionary<string, object> { { "name", "hello" }, { "damage", 123 } }
                };

                redis.Publish(matchId, "match", eventData);

                return HookResult.Continue;
            }
        );
    }
}
