using FiveStack.Utilities;
using FiveStack.Entities;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler(HookMode.Post)]
    public HookResult DecoyThrown(EventDecoyStarted @event)
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

        IPlayer thrower = @event.UserIdPlayer;

        _matchEvents.PublishGameEvent(
            "utility",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", match.GetActiveMapId() ?? matchData.current_match_map_id },
                { "round", _gameServer.GetCurrentRound() },
                { "type", "Decoy" },
                { "attacker_steam_id", thrower.SteamID.ToString() },
                { "attacker_location_coordinates", $"{@event.X},{@event.Y},{@event.Z}" },
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult GrenadeThrown(EventHegrenadeDetonate @event)
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

        IPlayer thrower = @event.UserIdPlayer;

        _matchEvents.PublishGameEvent(
            "utility",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", match.GetActiveMapId() ?? matchData.current_match_map_id },
                { "round", _gameServer.GetCurrentRound() },
                { "type", "HighExplosive" },
                { "attacker_steam_id", thrower.SteamID.ToString() },
                { "attacker_location_coordinates", $"{@event.X},{@event.Y},{@event.Z}" },
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult FlashBangThrown(EventFlashbangDetonate @event)
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

        IPlayer thrower = @event.UserIdPlayer;

        _matchEvents.PublishGameEvent(
            "utility",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", match.GetActiveMapId() ?? matchData.current_match_map_id },
                { "round", _gameServer.GetCurrentRound() },
                { "type", "Flash" },
                { "attacker_steam_id", thrower.SteamID.ToString() },
                { "attacker_location_coordinates", $"{@event.X},{@event.Y},{@event.Z}" },
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult MolotovThrown(EventMolotovDetonate @event)
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

        IPlayer thrower = @event.UserIdPlayer;

        _matchEvents.PublishGameEvent(
            "utility",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", match.GetActiveMapId() ?? matchData.current_match_map_id },
                { "round", _gameServer.GetCurrentRound() },
                { "type", "Molotov" },
                { "attacker_steam_id", thrower.SteamID.ToString() },
                { "attacker_location_coordinates", $"{@event.X},{@event.Y},{@event.Z}" },
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult SmokeThrown(EventSmokegrenadeDetonate @event)
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

        IPlayer thrower = @event.UserIdPlayer;

        _matchEvents.PublishGameEvent(
            "utility",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", match.GetActiveMapId() ?? matchData.current_match_map_id },
                { "round", _gameServer.GetCurrentRound() },
                { "type", "Smoke" },
                { "attacker_steam_id", thrower.SteamID.ToString() },
                { "attacker_location_coordinates", $"{@event.X},{@event.Y},{@event.Z}" },
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult PlayerBlinded(EventPlayerBlind @event)
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

        IPlayer blindedPlayer = @event.UserIdPlayer;
        IPlayer? attacker = @event.AttackerPlayer;

        if (attacker == null)
        {
            return HookResult.Continue;
        }

        _matchEvents.PublishGameEvent(
            "flash",
            new Dictionary<string, object>
            {
                { "time", DateTime.Now },
                { "match_map_id", match.GetActiveMapId() ?? matchData.current_match_map_id },
                { "round", _gameServer.GetCurrentRound() },
                { "attacker_steam_id", attacker.SteamID.ToString() },
                { "attacked_steam_id", blindedPlayer.SteamID.ToString() },
                { "duration", @event.BlindDuration },
                { "team_flash", attacker.Controller.TeamNum == blindedPlayer.Controller.TeamNum },
            }
        );

        return HookResult.Continue;
    }
}
