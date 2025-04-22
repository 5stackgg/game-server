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

        _surrenderSystem.CancelDisconnectTimer(@event.Userid.SteamID);

        CCSPlayerController player = @event.Userid;

        Guid? lineup_id = MatchUtility.GetPlayerLineup(matchData, player);
        List<MatchMember> players = matchData
            .lineup_1.lineup_players.Concat(matchData.lineup_2.lineup_players)
            .ToList();

        bool shouldKick = true;

        if (
            match.IsWarmup()
            && players.Any(player => !string.IsNullOrEmpty(player.placeholder_name))
        )
        {
            shouldKick = false;
        }

        if (players.Find(player => player.steam_id == null) != null)
        {
            shouldKick = false;
        }

        if (shouldKick && lineup_id == null)
        {
            Server.ExecuteCommand($"kickid {player.UserId}");
            return HookResult.Continue;
        }

        match.EnforceMemberTeam(player, CsTeam.None);

        _matchEvents.PublishGameEvent(
            "player-connected",
            new Dictionary<string, object>
            {
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

        if (MatchUtility.Players().Count == 1 && match.IsWarmup())
        {
            _gameServer.SendCommands(new[] { "mp_warmup_start" });
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

        if (_readySystem.IsWaitingForReady())
        {
            _gameServer.Message(
                HudDestination.Chat,
                $" {ChatColors.Default}type {ChatColors.Green}{CommandUtility.PublicChatTrigger}r {ChatColors.Default}to be marked as ready for the match",
                @event.Userid
            );
        }

        _gameServer.Message(
            HudDestination.Chat,
            $"type {ChatColors.Green}{CommandUtility.SilentChatTrigger}help {ChatColors.Default}to view additional commands",
            @event.Userid
        );

        if (MatchUtility.Rules()?.SwitchingTeamsAtRoundReset == true)
        {
            return HookResult.Continue;
        }

        match.EnforceMemberTeam(player, TeamUtility.TeamNumToCSTeam(@event.Team));

        return HookResult.Continue;
    }
}
