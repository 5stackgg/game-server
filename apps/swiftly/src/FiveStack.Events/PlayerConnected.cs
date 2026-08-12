using FiveStack.Entities;
using FiveStack.Utilities;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [GameEventHandler(HookMode.Post)]
    public HookResult OnPlayerConnect(EventPlayerConnectFull @event)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (
            @event.UserIdPlayer == null
            || !@event.UserIdPlayer.IsValid
            || @event.UserIdPlayer.IsFakeClient
            || match == null
            || matchData?.current_match_map_id == null
        )
        {
            return HookResult.Continue;
        }

        _surrenderSystem.CancelDisconnectTimer(@event.UserIdPlayer.SteamID);

        // CancelDisconnectTimer only resumes when that player actually had a
        // timer, which is never the case for someone who left during warmup or
        // knife, or when the pause came from RoundStart going short-handed. If
        // the roster is whole again there is nothing left to wait for.
        //
        // Counted across the roster rather than Players(), which also returns
        // casters, admins and the client kicked further down this handler -- a
        // spectator joining would otherwise resume a match still a man down. A
        // pause the teams have to release themselves is never auto-resumed.
        if (
            match.IsPaused()
            && !match.timeoutSystem.ShouldRequireTeamResume()
            && ConnectedRosterCount(matchData) >= match.GetExpectedPlayerCount()
        )
        {
            match.ResumeMatch();
        }

        IPlayer player = @event.UserIdPlayer;

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

        if (lineup_id == null)
        {
            string? role = null;
            if (PendingPlayers.ContainsKey(player.SteamID))
            {
                role = PendingPlayers[player.SteamID];
                player.Controller.Clan = $"[{role}]";
                player.Controller.ClanUpdated();
                PendingPlayers.Remove(player.SteamID);
            }

            if (shouldKick && role == null)
            {
                _core.Engine.ExecuteCommand($"kickid {player.UserID}");
                return HookResult.Continue;
            }
        }

        Team expectedTeam = match.GetExpectedTeam(player);
        int expectedTeamCount = match.GetExpectedPlayerCount() / 2;
        int teamCount = TeamUtility.GetTeamCount(expectedTeam);

        if (player.Controller.Team == expectedTeam)
        {
            teamCount--;
        }

        if (teamCount > expectedTeamCount)
        {
            _core.Engine.ExecuteCommand($"kickid {player.UserID}");
            return HookResult.Continue;
        }

        match.EnforceMemberTeam(player, Team.None);

        _matchEvents.PublishGameEvent(
            "player-connected",
            new Dictionary<string, object>
            {
                { "player_name", player.Name },
                { "steam_id", player.SteamID.ToString() },
            }
        );

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnPlayerJoinTeam(EventPlayerTeam @event)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (
            @event.UserIdPlayer == null
            || !@event.UserIdPlayer.IsValid
            || @event.UserIdPlayer.IsFakeClient
            || match == null
        )
        {
            return HookResult.Continue;
        }

        if (MatchUtility.PlayerCount() == 1 && match.IsWarmup())
        {
            _gameServer.SendCommands(["mp_warmup_start"]);
        }

        IPlayer player = @event.UserIdPlayer;

        if (match.readySystem.IsWaitingForReady())
        {
            _gameServer.Message(
                MessageType.Chat,
                _localizer[
                    "player.join.ready_hint",
                    "[green]",
                    CommandUtility.PublicChatTrigger,
                    "[default]"
                ],
                player
            );
        }

        _gameServer.Message(
            MessageType.Chat,
            _localizer[
                "player.join.help_hint",
                "[green]",
                CommandUtility.SilentChatTrigger,
                "[default]"
            ],
            player
        );

        return HookResult.Continue;
    }

    public HookResult HandleJoinTeam(IPlayer? player, string[] args)
    {
        if (player == null)
        {
            return HookResult.Continue;
        }

        if (args.Length < 2 || !int.TryParse(args[1], out int teamNum))
        {
            return HookResult.Continue;
        }

        Team joiningTeam = TeamUtility.TeamNumToTeam(teamNum);

        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null)
        {
            return HookResult.Continue;
        }

        Team expectedTeam = match.GetExpectedTeam(player);

        if (expectedTeam != Team.None && joiningTeam != expectedTeam)
        {
            return HookResult.Stop;
        }

        return HookResult.Continue;
    }

    // How many of the two lineups are actually in the server right now.
    private static int ConnectedRosterCount(MatchData matchData)
    {
        HashSet<string> roster = matchData
            .lineup_1.lineup_players.Concat(matchData.lineup_2.lineup_players)
            .Select(member => member.steam_id)
            .Where(steamId => !string.IsNullOrEmpty(steamId))
            .Select(steamId => steamId!)
            .ToHashSet();

        return MatchUtility
            .Players()
            .Count(player => roster.Contains(player.SteamID.ToString()));
    }

}
