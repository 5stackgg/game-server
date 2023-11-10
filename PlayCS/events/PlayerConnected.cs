using CounterStrikeSharp.API.Core;

namespace PlayCs;

public partial class PlayCsPlugin
{
    private void CapturePlayerConnected()
    {
        RegisterEventHandler<EventPlayerConnect>(
            (@event, info) =>
            {
                CCSPlayerController player = @event.Userid;
                Eventing.PublishMatchEvent(
                    matchData.id,
                    new Eventing.EventData<Dictionary<string, object>>
                    {
                        @event = "player",
                        data = new Dictionary<string, object>
                        {
                            { "steam_id", player.SteamID },
                            { "player_name", player.PlayerName },
                        }
                    }
                );

                // TODO - can we move them now?
                // player.SwitchTeam(team);

                return HookResult.Continue;
            }
        );
    }
}
