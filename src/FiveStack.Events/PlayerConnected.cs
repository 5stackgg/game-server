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
        MatchManager? match = CurrentMatch();
        MatchMap? currentMap = match?.GetCurrentMap();
        FiveStackMatch? matchData = match?.GetMatchData();

        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || @event.Userid.IsBot
            || match == null
            || matchData == null
            || currentMap == null
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController player = @event.Userid;

        if (match.IsLive())
        {
            Guid? lineup_id = MatchUtility.GetPlayerLineup(matchData, player);

            if (lineup_id == null)
            {
                Server.ExecuteCommand($"kick player {player.UserId}");
                return HookResult.Continue;
            }
        }

        _matchEvents.PublishGameEvent(
            "player",
            new Dictionary<string, object>
            {
                { "match_map_id", currentMap.id },
                { "player_name", player.PlayerName },
                { "steam_id", player.SteamID.ToString() },
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerJoinTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        MatchManager? match = CurrentMatch();
        MatchMap? currentMap = match?.GetCurrentMap();
        FiveStackMatch? matchData = match?.GetMatchData();

        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || @event.Userid.IsBot
            || match == null
            || matchData == null
            || currentMap == null
        )
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

        // TODO - if enforced, do we do silent?
        EnforceMemberTeam(matchData, currentMap, player, TeamUtility.TeamNumToCSTeam(@event.Team));

        return HookResult.Continue;
    }

    private async void EnforceMemberTeam(
        FiveStackMatch matchData,
        MatchMap currentMap,
        CCSPlayerController player,
        CsTeam currentTeam
    )
    {
        Guid? lineup_id = MatchUtility.GetPlayerLineup(matchData, player);

        if (lineup_id == null)
        {
            return;
        }

        CsTeam startingSide = TeamUtility.TeamStringToCsTeam(
            matchData.lineup_1_id == lineup_id ? currentMap.lineup_1_side : currentMap.lineup_2_side
        );

        Logger.LogInformation($"Current Team ${matchData.lineup_1_id}{currentTeam}:{startingSide}");
        if (currentTeam != startingSide)
        {
            // code smell: the server needs some time apparently
            await Task.Delay(1000 * 1);

            Server.NextFrame(() =>
            {
                player.ChangeTeam(startingSide);
                _gameServer.Message(
                    HudDestination.Chat,
                    $" You've been assigned to {(startingSide == CsTeam.Terrorist ? ChatColors.Gold : ChatColors.Blue)}{TeamUtility.CSTeamToString(startingSide)}.",
                    player
                );
            });
        }
    }
}
