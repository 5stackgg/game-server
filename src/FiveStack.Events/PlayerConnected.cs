using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;

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
            string? role = null;
            if (PendingPlayers.ContainsKey(player.SteamID))
            {
                role = PendingPlayers[player.SteamID];
                player.Clan = $"[{role}]";
                PendingPlayers.Remove(player.SteamID);
            }

            if (role == null || (role != "admin" && role != "organizer"))
            {
                Server.ExecuteCommand($"kickid {player.UserId}");
                return HookResult.Continue;
            }
        }

        CsTeam expectedTeam = match.GetExpectedTeam(player);
        int expectedTeamCount = match.GetExpectedPlayerCount() / 2;
        int teamCount = TeamUtility.GetTeamCount(expectedTeam);

        if (player.Team == expectedTeam)
        {
            teamCount--;
        }

        if (teamCount > expectedTeamCount)
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

        CCSPlayerController player = @event.Userid;

        if (_readySystem.IsWaitingForReady())
        {
            _gameServer.Message(
                HudDestination.Chat,
                Localizer[
                    "player.join.ready_hint",
                    ChatColors.Default,
                    ChatColors.Green,
                    CommandUtility.PublicChatTrigger,
                    ChatColors.Default
                ],
                player
            );
        }

        _gameServer.Message(
            HudDestination.Chat,
            Localizer[
                "player.join.help_hint",
                ChatColors.Green,
                CommandUtility.SilentChatTrigger,
                ChatColors.Default
            ],
            player
        );

        return HookResult.Continue;
    }

    public HookResult HandleJoinTeam(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return HookResult.Continue;
        }

        CsTeam joiningTeam = TeamUtility.TeamNumToCSTeam(int.Parse(info.ArgByIndex(1)));

        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null)
        {
            return HookResult.Continue;
        }

        CsTeam expectedTeam = match.GetExpectedTeam(player);

        if (expectedTeam != CsTeam.None && joiningTeam != expectedTeam)
        {
            return HookResult.Stop;
        }

        return HookResult.Continue;
    }
}
