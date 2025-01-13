using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using FiveStack.Entities;
using FiveStack.Utilities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnPlayerDamage(EventPlayerHurt @event, GameEventInfo info)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || match == null
            || matchData?.current_match_map_id == null
            || match.IsLive() == false
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController? attacker =
            @event.Attacker != null && @event.Attacker.IsValid ? @event.Attacker : null;

        CCSPlayerController attacked = @event.Userid;

        if (attacked.PlayerPawn.Value == null)
        {
            return HookResult.Continue;
        }

        var attackerLocation = attacker?.PlayerPawn.Value?.AbsOrigin;
        var attackedLocation = attacked?.PlayerPawn?.Value?.AbsOrigin;

        var damageDealt = @event.DmgHealth;

        if (attacked != null)
        {
            if (attacked.PlayerPawn.Value.Health < 0)
            {
                damageDealt = damageDealt + attacked.PlayerPawn.Value.Health;
            }
        }

        _matchEvents.PublishGameEvent(
            "damage",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", matchData.current_match_map_id },
                { "round", _gameServer.GetCurrentRound() },
                { "attacker_steam_id", attacker != null ? attacker.SteamID.ToString() : "" },
                {
                    "attacker_team",
                    attacker != null ? $"{TeamUtility.TeamNumToString(attacker.TeamNum)}" : ""
                },
                { "attacker_location", $"{attacker?.PlayerPawn?.Value?.LastPlaceName}" },
                {
                    "attacker_location_coordinates",
                    attackerLocation != null
                        ? $"{Convert.ToInt32(attackerLocation.X)} {Convert.ToInt32(attackerLocation.Y)} {Convert.ToInt32(attackerLocation.Z)}"
                        : ""
                },
                { "weapon", $"{(@event.Weapon.Length == 0 ? "worldent" : @event.Weapon)}" },
                { "damage", damageDealt },
                { "damage_armor", @event.DmgArmor },
                { "hitgroup", $"{DamageUtility.HitGroupToString(@event.Hitgroup)}" },
                { "health", @event.Health },
                { "armor", @event.Armor },
                { "attacked_steam_id", attacked != null ? attacked.SteamID.ToString() : "" },
                {
                    "attacked_team",
                    attacked != null ? $"{TeamUtility.TeamNumToString(attacked.TeamNum)}" : ""
                },
                { "attacked_location", $"{attacked?.PlayerPawn.Value.LastPlaceName}" },
                {
                    "attacked_location_coordinates",
                    attackedLocation != null
                        ? $"{Convert.ToInt32(attackedLocation.X)} {Convert.ToInt32(attackedLocation.Y)} {Convert.ToInt32(attackedLocation.Z)}"
                        : ""
                },
            }
        );

        return HookResult.Continue;
    }
}
