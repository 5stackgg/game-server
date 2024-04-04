using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult DecoyThrown(EventDecoyStarted @event, GameEventInfo info)
    {
        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || _matchData == null
            || _matchData.current_match_map_id == null
            || !IsLive()
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController thrower = @event.Userid;

        PublishGameEvent(
            "utility",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", _matchData.current_match_map_id },
                { "round", _currentRound + 1 },
                { "type", "Decoy" },
                { "attacker_steam_id", thrower.SteamID.ToString() },
                { "attacker_location_coordinates", $"{@event.X},{@event.Y},{@event.Z}" }
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult GrenadeThrown(EventHegrenadeDetonate @event, GameEventInfo info)
    {
        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || _matchData == null
            || _matchData.current_match_map_id == null
            || !IsLive()
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController thrower = @event.Userid;

        PublishGameEvent(
            "utility",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", _matchData.current_match_map_id },
                { "round", _currentRound + 1 },
                { "type", "HighExplosive" },
                { "attacker_steam_id", thrower.SteamID.ToString() },
                { "attacker_location_coordinates", $"{@event.X},{@event.Y},{@event.Z}" }
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult FlashBangThrown(EventFlashbangDetonate @event, GameEventInfo info)
    {
        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || _matchData == null
            || _matchData.current_match_map_id == null
            || !IsLive()
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController thrower = @event.Userid;

        PublishGameEvent(
            "utility",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", _matchData.current_match_map_id },
                { "round", _currentRound + 1 },
                { "type", "Flash" },
                { "attacker_steam_id", thrower.SteamID.ToString() },
                { "attacker_location_coordinates", $"{@event.X},{@event.Y},{@event.Z}" }
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult MolotovThrown(EventMolotovDetonate @event, GameEventInfo info)
    {
        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || _matchData == null
            || _matchData.current_match_map_id == null
            || !IsLive()
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController thrower = @event.Userid;

        PublishGameEvent(
            "utility",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", _matchData.current_match_map_id },
                { "round", _currentRound + 1 },
                { "type", "Molotov" },
                { "attacker_steam_id", thrower.SteamID.ToString() },
                { "attacker_location_coordinates", $"{@event.X},{@event.Y},{@event.Z}" }
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult SmokeThrown(EventSmokegrenadeDetonate @event, GameEventInfo info)
    {
        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || _matchData == null
            || _matchData.current_match_map_id == null
            || !IsLive()
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController thrower = @event.Userid;

        PublishGameEvent(
            "utility",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", _matchData.current_match_map_id },
                { "round", _currentRound + 1 },
                { "type", "Smoke" },
                { "attacker_steam_id", thrower.SteamID.ToString() },
                { "attacker_location_coordinates", $"{@event.X},{@event.Y},{@event.Z}" }
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult PlayerBlinded(EventPlayerBlind @event, GameEventInfo info)
    {
        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || _matchData == null
            || _matchData.current_match_map_id == null
            || !IsLive()
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController thrower = @event.Userid;
        CCSPlayerController attacked = @event.Attacker;

        PublishGameEvent(
            "flash",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", _matchData.current_match_map_id },
                { "round", _currentRound + 1 },
                { "attacker_steam_id", thrower.SteamID.ToString() },
                { "attacked_steam_id", attacked.SteamID.ToString() },
                { "duration", @event.BlindDuration },
                { "team_flash", thrower.TeamNum == attacked.TeamNum }
            }
        );

        return HookResult.Continue;
    }
}
