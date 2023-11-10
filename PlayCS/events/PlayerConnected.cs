using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using PlayCs.entities;

namespace PlayCs;

public partial class PlayCsPlugin
{
    private void CapturePlayerConnected()
    {
        RegisterEventHandler<EventPlayerConnect>(
            (@event, info) =>
            {
                CCSPlayerController player = @event.Userid;

                if (player.IsBot)
                {
                    return HookResult.Continue;
                }

                if (matchData == null)
                {
                    Console.WriteLine("We do not have any match data");
                    return HookResult.Continue;
                }

                Eventing.PublishMatchEvent(
                    matchData.id,
                    new Eventing.EventData<Dictionary<string, object>>
                    {
                        @event = "player",
                        data = new Dictionary<string, object>
                        {
                            { "steam_id", player.SteamID },
                            { "player_name", player.PlayerName },
                        }
                    }
                );

                MatchMember? foundMatchingMember = matchData
                    .members
                    .Find(member =>
                    {
                        if (member.steam_id == null)
                        {
                            return member.name.StartsWith(player.PlayerName);
                        }
                        return (ulong)member.steam_id == player.SteamID;
                    });

                if (foundMatchingMember != null)
                {
                    CsTeam startingSide = TeamStringToCsTeam(
                        foundMatchingMember.team.starting_side
                    );
                    if (TeamNumToCSTeam((int)player.TeamNum) != startingSide)
                    {
                        Console.WriteLine(
                            $"Switching {player.PlayerName} to {foundMatchingMember.team.starting_side}"
                        );
                        player.SwitchTeam(startingSide);
                    }
                }

                return HookResult.Continue;
            }
        );
    }
}
