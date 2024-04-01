using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.entities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler]
    public HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
    {
        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || @event.Userid.IsBot
            || _matchData == null
            || _matchData.current_match_map_id == null
        )
        {
            return HookResult.Continue;
        }

        CCSPlayerController player = @event.Userid;

        if (IsLive())
        {
            Guid? lineup_id = GetPlayerLineup(player);

            if (lineup_id == null)
            {
                Server.ExecuteCommand($"kickid {player.UserId}");
                return HookResult.Continue;
            }

            _enforceMemberTeam(player, CsTeam.Spectator);
        }

        PublishGameEvent(
            "player",
            new Dictionary<string, object>
            {
                { "match_map_id", _matchData.current_match_map_id },
                { "player_name", player.PlayerName },
                { "steam_id", player.SteamID.ToString() },
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerJoinTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        if (
            @event.Userid == null
            || !@event.Userid.IsValid
            || @event.Userid.IsBot
            || _matchData == null
        )
        {
            return HookResult.Continue;
        }

        // dont allow them to join a team
        if (
            _coaches[CsTeam.Terrorist] == @event.Userid
            || _coaches[CsTeam.CounterTerrorist] == @event.Userid
        )
        {
            @event.Silent = true;
            return HookResult.Changed;
        }

        CCSPlayerController player = @event.Userid;

        _enforceMemberTeam(player, TeamNumToCSTeam(@event.Team));

        Message(
            HudDestination.Chat,
            $"{ChatColors.Default}type {ChatColors.Green}.ready {ChatColors.Default}to be marked as ready for the match",
            @event.Userid
        );

        Message(
            HudDestination.Chat,
            $"type {ChatColors.Green}.help {ChatColors.Default}to view additional commands",
            @event.Userid
        );

        return HookResult.Continue;
    }

    private async void _enforceMemberTeam(CCSPlayerController player, CsTeam currentTeam)
    {
        if (_matchData == null || IsLive())
        {
            return;
        }

        var currentMap = GetCurrentMap();

        if (currentMap == null)
        {
            Logger.LogWarning("Unable to find map");
            return;
        }

        Guid? lineup_id = GetPlayerLineup(player);

        if (lineup_id == null)
        {
            return;
        }

        CsTeam startingSide = TeamStringToCsTeam(
            _matchData.lineup_1_id == lineup_id
                ? currentMap.lineup_1_side
                : currentMap.lineup_2_side
        );

        Logger.LogInformation(
            $"Current Team ${_matchData.lineup_1_id}{currentTeam}:{startingSide}"
        );
        if (currentTeam != startingSide)
        {
            // the server needs some time apparently
            await Task.Delay(1000 * 1);

            Server.NextFrame(() =>
            {
                player.ChangeTeam(startingSide);
                Message(
                    HudDestination.Chat,
                    $" You've been assigned to {(startingSide == CsTeam.Terrorist ? ChatColors.Gold : ChatColors.Blue)}{CSTeamToString(startingSide)}.",
                    player
                );
            });
        }
    }
}
