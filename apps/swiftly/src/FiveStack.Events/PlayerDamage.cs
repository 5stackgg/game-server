using FiveStack.Entities;
using FiveStack.Utilities;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

public partial class FiveStackPlugin
{
#pragma warning disable CS0618
    [GameEventHandler(HookMode.Post)]
    public HookResult OnPlayerDamage(EventPlayerHurt @event)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (
            @event.UserIdPlayer == null
            || !@event.UserIdPlayer.IsValid
            || @event.UserIdPlayer.IsFakeClient
            || match == null
            || matchData?.current_match_map_id == null
            || !match.IsInPlay()
        )
        {
            return HookResult.Continue;
        }

        IPlayer? attacker =
            @event.AttackerPlayer != null && @event.AttackerPlayer.IsValid
                ? @event.AttackerPlayer
                : null;

        IPlayer attacked = @event.UserIdPlayer;

        CCSPlayerPawn? attackedPawn = attacked.PlayerPawn;
        if (attackedPawn == null)
        {
            return HookResult.Continue;
        }

        CCSPlayerPawn? attackerPawn = attacker?.PlayerPawn;

        var attackerLocation = attackerPawn?.AbsOrigin;
        var attackedLocation = attackedPawn.AbsOrigin;

        int damageDealt = @event.DmgHealth;

        if (attackedPawn.Health < 0)
        {
            damageDealt = damageDealt + attackedPawn.Health;
        }

        _matchEvents.PublishGameEvent(
            "damage",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", match.GetActiveMapId() ?? matchData.current_match_map_id },
                { "round", _gameServer.GetCurrentRound() },
                { "attacker_steam_id", attacker != null ? attacker.SteamID.ToString() : "" },
                {
                    "attacker_team",
                    attacker != null ? TeamUtility.TeamNumToString(attacker.Controller.TeamNum) : ""
                },
                { "attacker_location", attackerPawn?.LastPlaceName ?? "" },
                {
                    "attacker_location_coordinates",
                    attackerLocation != null
                        ? $"{Convert.ToInt32(attackerLocation.Value.X)} {Convert.ToInt32(attackerLocation.Value.Y)} {Convert.ToInt32(attackerLocation.Value.Z)}"
                        : ""
                },
                { "weapon", @event.Weapon.Length == 0 ? "worldent" : @event.Weapon },
                { "damage", damageDealt },
                { "damage_armor", @event.DmgArmor },
                { "hitgroup", DamageUtility.HitGroupToString((int)@event.ActualHitGroup) },
                { "health", @event.Health },
                { "armor", @event.Armor },
                { "attacked_steam_id", attacked.SteamID.ToString() },
                { "attacked_team", TeamUtility.TeamNumToString(attacked.Controller.TeamNum) },
                { "attacked_location", attackedPawn.LastPlaceName ?? "" },
                {
                    "attacked_location_coordinates",
                    attackedLocation != null
                        ? $"{Convert.ToInt32(attackedLocation.Value.X)} {Convert.ToInt32(attackedLocation.Value.Y)} {Convert.ToInt32(attackedLocation.Value.Z)}"
                        : ""
                },
            }
        );

        return HookResult.Continue;
    }
#pragma warning restore CS0618
}
