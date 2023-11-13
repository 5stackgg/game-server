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

        /*
        var absOrigin = player.PlayerPawn.Value.AbsOrigin;
        //* var absRotation = player.PlayerPawn.Value.AbsRotation;
        var spawnX = Convert.ToInt32(absOrigin.X);
        var spawnY = Convert.ToInt32(absOrigin.Y);
        var spawnZ = Convert.ToInt32(absOrigin.Z);
        Console.WriteLine($"{player.PlayerName} spawned at {absOrigin}");
        Console.WriteLine($"{spawnX}");
        Console.WriteLine($"{spawnY}");
        Console.WriteLine($"{spawnZ}");
        */

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
                    // { "attacker_location_vector", $"{@event.Attacker.PlayerPawn.Value.Controller.Value.}"},
                    // ‘CBodyComponent?.SceneNode?.AbsOrigin’
                    { "weapon", $"{@event.Weapon}" },
                    { "damage", @event.DmgHealth },
                    { "damage_armor", @event.DmgArmor },
                    { "hitgroup", $"{HitGroupToString(@event.Hitgroup)}" },
                    { "health", @event.Health },
                    { "armor", @event.Armor },
                    { "attacked_steam_id", attacked.SteamID.ToString() },
                    { "attacked_team", $"{TeamNumToString(attacked.TeamNum)}" },
                    { "attacked_location", $"{attacked.PlayerPawn.Value.LastPlaceName}" },
                }
            }
        );

        return HookResult.Continue;
    }
}