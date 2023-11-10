using CounterStrikeSharp.API.Core;

namespace PlayCs;

public partial class PlayCsPlugin
{
    private void CapturePlayerConnected()
    {
        RegisterEventHandler<EventPlayerConnect>(
            (@event, info) =>
            {
                Eventing.PublishMatchEvent(
                    matchData.id,
                    new Eventing.EventData<Dictionary<string, object>>
                    {
                        @event = "player",
                        data = new Dictionary<string, object>
                        {
                            { "steam_id", @event.Userid.SteamID },
                            { "player_name", @event.Userid.PlayerName },
                        }
                    }
                );
                
                // TODO - can we move them now?
                
                return HookResult.Continue;
            }
        );
    }
}