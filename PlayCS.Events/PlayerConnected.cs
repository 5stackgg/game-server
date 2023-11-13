using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using PlayCs.entities;

namespace PlayCs;

public partial class PlayCsPlugin
{
    [GameEventHandler]
    public HookResult OnPlayerConnect(EventPlayerConnect @event, GameEventInfo info)
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

        CCSPlayerController player = @event.Userid;

        _redis.PublishMatchEvent(
            _matchData.id,
            new Redis.EventData<Dictionary<string, object>>
            {
                @event = "player",
                data = new Dictionary<string, object>
                {
                    { "player_name", player.PlayerName },
                    { "steam_id", player.SteamID.ToString() },
                }
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerFullConnect(EventPlayerConnectFull @event, GameEventInfo info)
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

        Message(
            HudDestination.Chat,
            $"{ChatColors.Default}type {ChatColors.Green}!ready {ChatColors.Default}to be marked as ready for the match",
            @event.Userid
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

        CCSPlayerController player = @event.Userid;

        _enforceMemberTeam(player);

        return HookResult.Continue;
    }

    private async void _enforceMemberTeam(CCSPlayerController player)
    {
        if (_matchData == null)
        {
            return;
        }

        // the server needs some time apparently
        await Task.Delay(3000);

        MatchMember? foundMatchingMember = _matchData
            .members
            .Find(member =>
            {
                if (member.steam_id == null)
                {
                    return member.name.StartsWith(player.PlayerName);
                }
                return member.steam_id == player.SteamID.ToString();
            });

        if (foundMatchingMember != null)
        {
            MatchTeam? team = _matchData
                .teams
                .Find(team =>
                {
                    return team.id == foundMatchingMember.team_id;
                });

            if (team != null)
            {
                CsTeam startingSide = TeamStringToCsTeam(team.starting_side);
                if (TeamNumToCSTeam(player.TeamNum) != startingSide)
                {
                    Console.WriteLine($"Switching {player.PlayerName} to {team.starting_side}");
                    // TODO - its fucked
                    // player.ChangeTeam(startingSide);
                    Message(
                        HudDestination.Chat,
                        $" You've been assigned to {(startingSide == CsTeam.Terrorist ? ChatColors.Gold : ChatColors.Blue)}{team.starting_side}.",
                        player
                    );
                }
            }
        }
    }
}
