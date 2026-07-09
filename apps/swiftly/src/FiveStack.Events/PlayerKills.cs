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
    [GameEventHandler(HookMode.Post)]
    public HookResult OnPlayerKill(EventPlayerDeath @event)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (
            @event.UserIdPlayer == null
            || match == null
            || matchData?.current_match_map_id == null
            || !@event.UserIdPlayer.IsValid
            || @event.UserIdPlayer.IsFakeClient
        )
        {
            return HookResult.Continue;
        }

        if (MatchUtility.Rules()?.FreezePeriod == true)
        {
            return HookResult.Continue;
        }

        IPlayer attacked = @event.UserIdPlayer;

        if (match.IsWarmup() && attacked.Controller.InGameMoneyServices != null)
        {
            attacked.Controller.InGameMoneyServices.Account = 60000;
            return HookResult.Continue;
        }

        if (!match.IsInPlay())
        {
            return HookResult.Continue;
        }

        IPlayer? attacker =
            @event.AttackerPlayer != null && @event.AttackerPlayer.IsValid
                ? @event.AttackerPlayer
                : null;

        CCSPlayerPawn? attackerPawn = attacker?.PlayerPawn;
        CCSPlayerPawn? attackedPawn = attacked.PlayerPawn;

        var attackerLocation = attackerPawn?.AbsOrigin;
        var attackedLocation = attackedPawn?.AbsOrigin;

        _matchEvents.PublishGameEvent(
            "kill",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", match.GetActiveMapId() ?? matchData.current_match_map_id },
                { "no_scope", @event.NoScope },
                { "blinded", @event.AttackerBlind },
                { "thru_smoke", @event.ThruSmoke },
                { "thru_wall", @event.Penetrated > 0 },
                { "headshot", @event.Headshot },
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
                { "weapon", @event.Weapon },
                { "hitgroup", DamageUtility.HitGroupToString((int)@event.ActualHitGroup) },
                { "attacked_steam_id", attacked.SteamID.ToString() },
                { "attacked_team", TeamUtility.TeamNumToString(attacked.Controller.TeamNum) },
                { "attacked_location", attackedPawn?.LastPlaceName ?? "" },
                {
                    "attacked_location_coordinates",
                    attackedLocation != null
                        ? $"{Convert.ToInt32(attackedLocation.Value.X)} {Convert.ToInt32(attackedLocation.Value.Y)} {Convert.ToInt32(attackedLocation.Value.Z)}"
                        : ""
                },
            }
        );

        IPlayer? assister = @event.AssisterPlayer;

        if (attacker != null && assister != null && assister.IsValid)
        {
            if (attacker.Controller.TeamNum != attacked.Controller.TeamNum)
            {
                _matchEvents.PublishGameEvent(
                    "assist",
                    new Dictionary<string, object>
                    {
                        { "time", DateTime.Now },
                        {
                            "match_map_id",
                            match.GetActiveMapId() ?? matchData.current_match_map_id
                        },
                        { "round", _gameServer.GetCurrentRound() },
                        { "attacker_steam_id", assister.SteamID.ToString() },
                        { "attacker_team", TeamUtility.TeamNumToString(attacker.Controller.TeamNum) },
                        { "attacked_steam_id", attacked.SteamID.ToString() },
                        { "attacked_team", TeamUtility.TeamNumToString(attacked.Controller.TeamNum) },
                        { "flash", @event.AssistedFlash },
                    }
                );
            }
        }

        return HookResult.Continue;
    }
}
