using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using FiveStack.Entities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult DecoyThrown(EventDecoyStarted @event, GameEventInfo info)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || @event.Userid.IsBot
            || match == null
            || matchData?.current_match_map_id == null
            || !match.IsInProgress()
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController thrower = @event.Userid;

        _matchEvents.PublishGameEvent(
            "utility",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", matchData.current_match_map_id },
                { "round", _gameServer.GetCurrentRound() },
                { "type", "Decoy" },
                { "attacker_steam_id", thrower.SteamID.ToString() },
                { "attacker_location_coordinates", $"{@event.X},{@event.Y},{@event.Z}" },
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult GrenadeThrown(EventHegrenadeDetonate @event, GameEventInfo info)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || @event.Userid.IsBot
            || match == null
            || matchData?.current_match_map_id == null
            || !match.IsInProgress()
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController thrower = @event.Userid;

        _matchEvents.PublishGameEvent(
            "utility",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", matchData.current_match_map_id },
                { "round", _gameServer.GetCurrentRound() },
                { "type", "HighExplosive" },
                { "attacker_steam_id", thrower.SteamID.ToString() },
                { "attacker_location_coordinates", $"{@event.X},{@event.Y},{@event.Z}" },
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult FlashBangThrown(EventFlashbangDetonate @event, GameEventInfo info)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || @event.Userid.IsBot
            || match == null
            || matchData?.current_match_map_id == null
            || !match.IsInProgress()
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController thrower = @event.Userid;

        _matchEvents.PublishGameEvent(
            "utility",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", matchData.current_match_map_id },
                { "round", _gameServer.GetCurrentRound() },
                { "type", "Flash" },
                { "attacker_steam_id", thrower.SteamID.ToString() },
                { "attacker_location_coordinates", $"{@event.X},{@event.Y},{@event.Z}" },
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult MolotovThrown(EventMolotovDetonate @event, GameEventInfo info)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || @event.Userid.IsBot
            || match == null
            || matchData?.current_match_map_id == null
            || !match.IsInProgress()
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController thrower = @event.Userid;

        _matchEvents.PublishGameEvent(
            "utility",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", matchData.current_match_map_id },
                { "round", _gameServer.GetCurrentRound() },
                { "type", "Molotov" },
                { "attacker_steam_id", thrower.SteamID.ToString() },
                { "attacker_location_coordinates", $"{@event.X},{@event.Y},{@event.Z}" },
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult SmokeThrown(EventSmokegrenadeDetonate @event, GameEventInfo info)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || @event.Userid.IsBot
            || match == null
            || matchData?.current_match_map_id == null
            || !match.IsInProgress()
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController thrower = @event.Userid;

        _matchEvents.PublishGameEvent(
            "utility",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", matchData.current_match_map_id },
                { "round", _gameServer.GetCurrentRound() },
                { "type", "Smoke" },
                { "attacker_steam_id", thrower.SteamID.ToString() },
                { "attacker_location_coordinates", $"{@event.X},{@event.Y},{@event.Z}" },
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult PlayerBlinded(EventPlayerBlind @event, GameEventInfo info)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || @event.Userid.IsBot
            || match == null
            || matchData?.current_match_map_id == null
            || !match.IsInProgress()
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController blindedPlayer = @event.Userid;
        CCSPlayerController? attacker = @event.Attacker;

        if (attacker == null)
        {
            return HookResult.Continue;
        }

        _matchEvents.PublishGameEvent(
            "flash",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", matchData.current_match_map_id },
                { "round", _gameServer.GetCurrentRound() },
                { "attacker_steam_id", attacker.SteamID.ToString() },
                { "attacked_steam_id", blindedPlayer.SteamID.ToString() },
                { "duration", @event.BlindDuration },
                { "team_flash", attacker.TeamNum == blindedPlayer.TeamNum },
            }
        );

        return HookResult.Continue;
    }
}
