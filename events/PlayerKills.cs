using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;

namespace PlayCs;

public partial class PlayCsPlugin
{
    [GameEventHandler]
    public HookResult OnPlayerKill(EventPlayerDeath @event, GameEventInfo info)
    {
        if (_matchData == null)
        {
            return HookResult.Continue;
        }

        CCSPlayerController attacker = @event.Attacker;

        CCSPlayerController attacked = @event.Userid;

        _redis.PublishMatchEvent(
            _matchData.id,
            new Redis.EventData<Dictionary<string, object>>
            {
                @event = "kill",
                data = new Dictionary<string, object>
                {
                    { "round", _currentRound },
                    { "attacker_steam_id", attacker.SteamID },
                    { "attacker_team", $"{TeamNumToString(attacker.TeamNum)}" },
                    { "attacker_location", $"{attacker.PlayerPawn.Value.LastPlaceName}" },
                    // { "attacker_location_vector", $"{@event.Attacker.PlayerPawn.Value.Controller.Value.}"},
                    { "weapon", $"{@event.Weapon}" },
                    { "hitgroup", $"{HitGroupToString(@event.Hitgroup)}" },
                    { "attacked_steam_id", attacked.SteamID },
                    { "attacked_team", $"{TeamNumToString(attacked.TeamNum)}" },
                    { "attacked_location", $"{attacked.PlayerPawn.Value.LastPlaceName}" },
                }
            }
        );

        return HookResult.Continue;
    }
}
