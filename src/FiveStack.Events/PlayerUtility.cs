using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult DecoyThrown(EventDecoyStarted @event, GameEventInfo info)
    {
        if (@event.Userid == null || !@event.Userid.IsValid || _matchData == null || !IsLive())
        {
            return HookResult.Continue;
        }

        CCSPlayerController thrower = @event.Userid;

        _redis.PublishMatchEvent(
            _matchData.id,
            new Redis.EventData<Dictionary<string, object>>
            {
                @event = "utility",
                data = new Dictionary<string, object>
                {
                    { "match_map_id", _matchData.current_match_map_id },
                    { "round", _currentRound + 1 },
                    { "type", "decoy" },
                    { "attacker_steam_id", thrower.SteamID.ToString() },
                    { "location_coordinates", $"{@event.X},{@event.Y},{@event.Z}" },
                }
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult GrenadeThrown(EventHegrenadeDetonate @event, GameEventInfo info)
    {
        if (@event.Userid == null || !@event.Userid.IsValid || _matchData == null || !IsLive())
        {
            return HookResult.Continue;
        }

        CCSPlayerController thrower = @event.Userid;

        _redis.PublishMatchEvent(
            _matchData.id,
            new Redis.EventData<Dictionary<string, object>>
            {
                @event = "utility",
                data = new Dictionary<string, object>
                {
                    { "match_map_id", _matchData.current_match_map_id },
                    { "round", _currentRound + 1 },
                    { "type", "he" },
                    { "attacker_steam_id", thrower.SteamID.ToString() },
                    { "location_coordinates", $"{@event.X},{@event.Y},{@event.Z}" }
                }
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult FlashBangThrown(EventFlashbangDetonate @event, GameEventInfo info)
    {
        if (@event.Userid == null || !@event.Userid.IsValid || _matchData == null || !IsLive())
        {
            return HookResult.Continue;
        }

        CCSPlayerController thrower = @event.Userid;

        _redis.PublishMatchEvent(
            _matchData.id,
            new Redis.EventData<Dictionary<string, object>>
            {
                @event = "utility",
                data = new Dictionary<string, object>
                {
                    { "match_map_id", _matchData.current_match_map_id },
                    { "round", _currentRound + 1 },
                    { "type", "flash" },
                    { "attacker_steam_id", thrower.SteamID.ToString() },
                    { "location_coordinates", $"{@event.X},{@event.Y},{@event.Z}" }
                }
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult MolotovThrown(EventMolotovDetonate @event, GameEventInfo info)
    {
        if (@event.Userid == null || !@event.Userid.IsValid || _matchData == null || !IsLive())
        {
            return HookResult.Continue;
        }

        CCSPlayerController thrower = @event.Userid;

        _redis.PublishMatchEvent(
            _matchData.id,
            new Redis.EventData<Dictionary<string, object>>
            {
                @event = "utility",
                data = new Dictionary<string, object>
                {
                    { "match_map_id", _matchData.current_match_map_id },
                    { "round", _currentRound + 1 },
                    { "type", "molotov" },
                    { "attacker_steam_id", thrower.SteamID.ToString() },
                    { "location_coordinates", $"{@event.X},{@event.Y},{@event.Z}" }
                }
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult SmokeThrown(EventSmokegrenadeDetonate @event, GameEventInfo info)
    {
        if (@event.Userid == null || !@event.Userid.IsValid || _matchData == null || !IsLive())
        {
            return HookResult.Continue;
        }

        CCSPlayerController thrower = @event.Userid;

        _redis.PublishMatchEvent(
            _matchData.id,
            new Redis.EventData<Dictionary<string, object>>
            {
                @event = "utility",
                data = new Dictionary<string, object>
                {
                    { "match_map_id", _matchData.current_match_map_id },
                    { "round", _currentRound + 1 },
                    { "type", "smoke" },
                    { "attacker_steam_id", thrower.SteamID.ToString() },
                    { "location_coordinates", $"{@event.X},{@event.Y},{@event.Z}" }
                }
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult PlayerBlinded(EventPlayerBlind @event, GameEventInfo info)
    {
        if (@event.Userid == null || !@event.Userid.IsValid || _matchData == null || !IsLive())
        {
            return HookResult.Continue;
        }

        CCSPlayerController thrower = @event.Userid;
        CCSPlayerController attacked = @event.Attacker;

        _redis.PublishMatchEvent(
            _matchData.id,
            new Redis.EventData<Dictionary<string, object>>
            {
                @event = "flash",
                data = new Dictionary<string, object>
                {
                    { "match_map_id", _matchData.current_match_map_id },
                    { "round", _currentRound + 1 },
                    { "type", "molotov" },
                    { "attacker_steam_id", thrower.SteamID.ToString() },
                    { "attacked_steam_id", attacked.SteamID.ToString() },
                    { "duration", @event.BlindDuration }
                }
            }
        );

        return HookResult.Continue;
    }
}
