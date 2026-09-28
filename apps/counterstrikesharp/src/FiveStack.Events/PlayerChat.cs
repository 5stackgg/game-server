using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;

namespace FiveStack;

public partial class FiveStackPlugin
{
    public HookResult OnPlayerChat(CCSPlayerController? player, CommandInfo info)
    {
        return HandlePlayerChat(player, info, false);
    }

    public HookResult OnPlayerTeamChat(CCSPlayerController? player, CommandInfo info)
    {
        return HandlePlayerChat(player, info, true);
    }

    private HookResult HandlePlayerChat(
        CCSPlayerController? player,
        CommandInfo info,
        bool teamOnly
    )
    {
        if (player == null || !player.IsValid)
        {
            return HookResult.Continue;
        }

        string message = info.ArgString.Trim('"');

        if (teamOnly)
        {
            RelayTeamChat(player, message);

            return HookResult.Continue;
        }

        if (player.Team == CsTeam.Spectator)
        {
            PublishChatEvent(player, message);

            string clan = string.IsNullOrEmpty(player.Clan) ? "" : $"[{player.Clan}]";

            _gameServer.Message(
                HudDestination.Chat,
                $" {ChatColors.Red}{clan}{ChatColors.White} {player.PlayerName}: {message}"
            );

            return HookResult.Stop;
        }

        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null)
        {
            return HookResult.Continue;
        }

        MatchData? matchData = match.GetMatchData();

        if (matchData == null)
        {
            return HookResult.Continue;
        }

        MatchMember? member = MatchUtility.GetMemberFromLineup(
            matchData,
            player.SteamID.ToString(),
            player.PlayerName
        );

        if (member != null)
        {
            if (member.is_gagged)
            {
                return HookResult.Stop;
            }
        }

        PublishChatEvent(player, message);

        return HookResult.Continue;
    }

    // CSS skips the remaining say_team listeners once one returns Stop, so the
    // gag is left to GagPlayer, which also tells the speaker they are gagged.
    private void RelayTeamChat(CCSPlayerController player, string message)
    {
        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();

        if (matchData == null || !matchData.relay_team_chat)
        {
            return;
        }

        string steamId = player.SteamID.ToString();

        MatchMember? member = MatchUtility.GetMemberFromLineup(
            matchData,
            steamId,
            player.PlayerName
        );

        if (member != null && member.is_gagged)
        {
            return;
        }

        string? lineupId = MatchUtility.GetTeamChatLineupId(matchData, steamId, player.PlayerName);

        if (lineupId == null)
        {
            return;
        }

        PublishChatEvent(player, message, lineupId);
    }

    private void PublishChatEvent(
        CCSPlayerController player,
        string message,
        string? teamLineupId = null
    )
    {
        Dictionary<string, object> data = new Dictionary<string, object>
        {
            { "player", player.SteamID.ToString() },
            { "message", message },
        };

        if (teamLineupId != null)
        {
            data["teamOnly"] = true;
            data["lineupId"] = teamLineupId;
        }

        _matchEvents.PublishGameEvent("chat", data);
    }
}
