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
                if (@event.Userid == null || @event.Userid.IsBot || _matchData == null)
                {
                    return HookResult.Continue;
                }

                CCSPlayerController? player = @event.Userid;

                _redis.PublishMatchEvent(
                    _matchData.id,
                    new Redis.EventData<Dictionary<string, object>>
                    {
                        @event = "player",
                        data = new Dictionary<string, object>
                        {
                            { "steam_id", player.SteamID },
                            { "player_name", player.PlayerName },
                        }
                    }
                );

                // MatchMember? foundMatchingMember = matchData
                //     .members
                //     .Find(member =>
                //     {
                //         if (member.steam_id == null)
                //         {
                //             return member.name.StartsWith(player.PlayerName);
                //         }
                //         return (ulong)member.steam_id == player.SteamID;
                //     });

                // if (foundMatchingMember != null)
                // {
                //     MatchTeam? team = matchData
                //         .teams
                //         .Find(team =>
                //         {
                //             return team.id == foundMatchingMember.team_id;
                //         });
                //
                //     if (team != null)
                //     {
                //         CsTeam startingSide = TeamStringToCsTeam(team.starting_side);
                //         if (TeamNumToCSTeam((int)player.TeamNum) != startingSide)
                //         {
                //             Console.WriteLine(
                //                 $"Switching {player.PlayerName} to {team.starting_side}"
                //             );
                //             player.SwitchTeam(startingSide);
                //         }
                //     }
                // }

                return HookResult.Continue;
            }
        );
    }
}
