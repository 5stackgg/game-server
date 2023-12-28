using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using PlayCS.enums;

namespace PlayCs;

public partial class PlayCsPlugin
{
    [GameEventHandler]
    public HookResult OnPlayerConnect(EventPlayerHurt @event, GameEventInfo info)
    {
        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || @event.Userid.IsBot
            || _matchData == null
            || !IsLive()
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController attacker = @event.Attacker;

        CCSPlayerController attacked = @event.Userid;

        if (attacker.PlayerPawn.Value != null && attacked.PlayerPawn.Value != null)
        {
            var attackerLocation = attacker.PlayerPawn.Value.AbsOrigin;
            var attackedLocation = attacked.PlayerPawn.Value.AbsOrigin;

            _redis.PublishMatchEvent(
                _matchData.id,
                new Redis.EventData<Dictionary<string, object>>
                {
                    @event = "damage",
                    data = new Dictionary<string, object>
                    {
                        { "round", _currentRound },
                        { "attacker_steam_id", attacker.SteamID.ToString() },
                        { "attacker_team", $"{TeamNumToString(attacker.TeamNum)}" },
                        { "attacker_location", $"{attacker.PlayerPawn.Value.LastPlaceName}" },
                        {
                            "attacker_location_coordinates",
                            attackerLocation != null
                                ? $"{Convert.ToInt32(attackerLocation.X)} {Convert.ToInt32(attackerLocation.Y)} {Convert.ToInt32(attackerLocation.Z)}"
                                : ""
                        },
                        { "weapon", $"{@event.Weapon}" },
                        { "damage", @event.DmgHealth },
                        { "damage_armor", @event.DmgArmor },
                        { "hitgroup", $"{HitGroupToString(@event.Hitgroup)}" },
                        { "health", @event.Health },
                        { "armor", @event.Armor },
                        { "attacked_steam_id", attacked.SteamID.ToString() },
                        { "attacked_team", $"{TeamNumToString(attacked.TeamNum)}" },
                        { "attacked_location", $"{attacked.PlayerPawn.Value.LastPlaceName}" },
                        {
                            "attacked_location_coordinates",
                            attackedLocation != null
                                ? $"{Convert.ToInt32(attackedLocation.X)} {Convert.ToInt32(attackedLocation.Y)} {Convert.ToInt32(attackedLocation.Z)}"
                                : ""
                        },
                    }
                }
            );
        }

        return HookResult.Continue;
    }
}
