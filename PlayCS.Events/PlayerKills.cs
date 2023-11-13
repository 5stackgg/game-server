using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;

namespace PlayCs;

public partial class PlayCsPlugin
{
    [GameEventHandler]
    public HookResult OnPlayerKill(EventPlayerDeath @event, GameEventInfo info)
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

        var attackerLocation = attacker.PlayerPawn.Value.AbsOrigin;
        var attackedLocation = attacked.PlayerPawn.Value.AbsOrigin;

        _redis.PublishMatchEvent(
            _matchData.id,
            new Redis.EventData<Dictionary<string, object>>
            {
                @event = "kill",
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
                    { "hitgroup", $"{HitGroupToString(@event.Hitgroup)}" },
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

        CCSPlayerController? assister = @event.Assister;

        if (assister != null && assister.IsValid)
        {
            if (attacker.TeamNum != attacked.TeamNum)
            {
                _redis.PublishMatchEvent(
                    _matchData.id,
                    new Redis.EventData<Dictionary<string, object>>
                    {
                        @event = "assist",
                        data = new Dictionary<string, object>
                        {
                            { "match_id", _matchData.id },
                            { "round", _currentRound },
                            { "attacker_steam_id", assister.SteamID.ToString() },
                            { "attacker_team", $"{TeamNumToString(attacker.TeamNum)}" },
                            { "attacked_steam_id", attacked.SteamID.ToString() },
                            { "attacked_team", $"{TeamNumToString(attacked.TeamNum)}" },
                            { "flash", @event.Assistedflash },
                        }
                    }
                );
            }
        }

        return HookResult.Continue;
    }
}
