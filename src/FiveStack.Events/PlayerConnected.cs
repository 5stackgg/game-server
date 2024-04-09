using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || @event.Userid.IsBot
            || match == null
            || matchData?.current_match_map_id == null
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController player = @event.Userid;

        Guid? lineup_id = MatchUtility.GetPlayerLineup(matchData, player);

        if (lineup_id == null && match.IsWarmup() == false)
        {
            Server.ExecuteCommand($"kickid {player.UserId}");
            return HookResult.Continue;
        }

        _matchEvents.PublishGameEvent(
            "player",
            new Dictionary<string, object>
            {
                { "match_map_id", matchData.current_match_map_id },
                { "player_name", player.PlayerName },
                { "steam_id", player.SteamID.ToString() },
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerJoinTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (@event.Userid == null || !@event.Userid.IsValid || @event.Userid.IsBot || match == null)
        {
            return HookResult.Continue;
        }

        // TODO - coaches
        // dont allow them to join a team
        // if (
        //     _coaches[CsTeam.Terrorist] == @event.Userid
        //     || _coaches[CsTeam.CounterTerrorist] == @event.Userid
        // )
        // {
        //     @event.Silent = true;
        //     return HookResult.Changed;
        // }

        CCSPlayerController player = @event.Userid;

        _gameServer.Message(
            HudDestination.Chat,
            $"{ChatColors.Default}type {ChatColors.Green}.ready {ChatColors.Default}to be marked as ready for the match",
            @event.Userid
        );

        _gameServer.Message(
            HudDestination.Chat,
            $"type {ChatColors.Green}.help {ChatColors.Default}to view additional commands",
            @event.Userid
        );

        if (!match.IsLive())
        {
            match.EnforceMemberTeam(player, TeamUtility.TeamNumToCSTeam(@event.Team));
        }

        return HookResult.Continue;
    }
}
