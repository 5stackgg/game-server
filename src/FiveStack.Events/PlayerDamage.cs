// using CounterStrikeSharp.API.Core;
// using CounterStrikeSharp.API.Core.Attributes.Registration;
// using Microsoft.Extensions.Logging;

// namespace FiveStack;

// public partial class FiveStackPlugin
// {
//     [GameEventHandler]
//     public HookResult OnPlayerDamage(EventPlayerHurt @event, GameEventInfo info)
//     {
//         if (
//             @event.Userid == null
//             || !@event.Userid.IsValid
//             || _matchData == null
//             || _matchData.current_match_map_id == null
//             || !IsLive()
//         )
//         {
//             return HookResult.Continue;
//         }

//         CCSPlayerController? attacker = @event.Attacker.IsValid ? @event.Attacker : null;

//         CCSPlayerController attacked = @event.Userid;

//         if (attacked.PlayerPawn.Value == null)
//         {
//             return HookResult.Continue;
//         }

//         var attackerLocation = attacker?.PlayerPawn.Value?.AbsOrigin;
//         var attackedLocation = attacked?.PlayerPawn?.Value?.AbsOrigin;

//         PublishGameEvent(
//             "damage",
//             new Dictionary<string, object>
//             {
//                 { "time", DateTime.Now },
//                 { "match_map_id", _matchData.current_match_map_id },
//                 { "round", _currentRound },
//                 { "attacker_steam_id", attacker != null ? attacker.SteamID.ToString() : "" },
//                 { "attacker_team", attacker != null ? $"{TeamNumToString(attacker.TeamNum)}" : "" },
//                 { "attacker_location", $"{attacker?.PlayerPawn?.Value?.LastPlaceName}" },
//                 {
//                     "attacker_location_coordinates",
//                     attackerLocation != null
//                         ? $"{Convert.ToInt32(attackerLocation.X)} {Convert.ToInt32(attackerLocation.Y)} {Convert.ToInt32(attackerLocation.Z)}"
//                         : ""
//                 },
//                 { "weapon", $"{@event.Weapon}" },
//                 { "damage", @event.DmgHealth },
//                 { "damage_armor", @event.DmgArmor },
//                 { "hitgroup", $"{HitGroupToString(@event.Hitgroup)}" },
//                 { "health", @event.Health },
//                 { "armor", @event.Armor },
//                 { "attacked_steam_id", attacked != null ? attacked.SteamID.ToString() : "" },
//                 { "attacked_team", attacked != null ? $"{TeamNumToString(attacked.TeamNum)}" : "" },
//                 { "attacked_location", $"{attacked?.PlayerPawn.Value.LastPlaceName}" },
//                 {
//                     "attacked_location_coordinates",
//                     attackedLocation != null
//                         ? $"{Convert.ToInt32(attackedLocation.X)} {Convert.ToInt32(attackedLocation.Y)} {Convert.ToInt32(attackedLocation.Z)}"
//                         : ""
//                 },
//             }
//         );

//         return HookResult.Continue;
//     }
// }
