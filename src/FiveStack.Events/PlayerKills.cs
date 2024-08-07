using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using FiveStack.Entities;
using FiveStack.Utilities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnPlayerKill(EventPlayerDeath @event, GameEventInfo info)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (@event.Userid == null || match == null || !@event.Userid.IsValid || @event.Userid.IsBot)
        {
            return HookResult.Continue;
        }

        CCSPlayerController attacked = @event.Userid;

        if (match.IsWarmup() && attacked.InGameMoneyServices != null)
        {
            attacked.InGameMoneyServices.Account = 60000;
            return HookResult.Continue;
        }

        if (matchData?.current_match_map_id == null || match.IsLive() == false)
        {
            return HookResult.Continue;
        }

        CCSPlayerController? attacker =
            @event.Attacker != null && @event.Attacker.IsValid ? @event.Attacker : null;

        var attackerLocation = attacker?.PlayerPawn?.Value?.AbsOrigin;
        var attackedLocation = attacked?.PlayerPawn?.Value?.AbsOrigin;

        _matchEvents.PublishGameEvent(
            "kill",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", matchData.current_match_map_id },
                { "no_scope", @event.Noscope },
                { "blinded", @event.Attackerblind },
                { "thru_smoke", @event.Thrusmoke },
                { "thru_wall", @event.Penetrated > 0 },
                { "headshot", @event.Headshot },
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
                { "weapon", $"{@event.Weapon}" },
                { "hitgroup", $"{DamageUtility.HitGroupToString(@event.Hitgroup)}" },
                { "attacked_steam_id", attacked != null ? attacked.SteamID.ToString() : "" },
                {
                    "attacked_team",
                    attacked != null ? $"{TeamUtility.TeamNumToString(attacked.TeamNum)}" : ""
                },
                { "attacked_location", $"{attacked?.PlayerPawn?.Value?.LastPlaceName}" },
                {
                    "attacked_location_coordinates",
                    attackedLocation != null
                        ? $"{Convert.ToInt32(attackedLocation.X)} {Convert.ToInt32(attackedLocation.Y)} {Convert.ToInt32(attackedLocation.Z)}"
                        : ""
                },
            }
        );

        CCSPlayerController? assister = @event.Assister;

        if (attacker != null && attacked != null && assister != null && assister.IsValid)
        {
            if (attacker.TeamNum != attacked.TeamNum)
            {
                _matchEvents.PublishGameEvent(
                    "assist",
                    new Dictionary<string, object>
                    {
                        { "time", DateTime.Now },
                        { "match_map_id", matchData.current_match_map_id },
                        { "round", _gameServer.GetCurrentRound() },
                        { "attacker_steam_id", assister.SteamID.ToString() },
                        { "attacker_team", $"{TeamUtility.TeamNumToString(attacker.TeamNum)}" },
                        { "attacked_steam_id", attacked.SteamID.ToString() },
                        { "attacked_team", $"{TeamUtility.TeamNumToString(attacked.TeamNum)}" },
                        { "flash", @event.Assistedflash },
                    }
                );
            }
        }

        return HookResult.Continue;
    }
}
