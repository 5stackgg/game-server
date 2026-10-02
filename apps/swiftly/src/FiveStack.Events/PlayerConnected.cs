using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

public partial class FiveStackPlugin
{
    private readonly HashSet<ulong> _overCapacityKicks = new();

    [GameEventHandler(HookMode.Post)]
    public HookResult OnPlayerConnect(EventPlayerConnectFull @event)
    {
        if (
            @event.UserIdPlayer == null
            || !@event.UserIdPlayer.IsValid
            || @event.UserIdPlayer.IsFakeClient
        )
        {
            return HookResult.Continue;
        }

        ClearCommunicationAbuseMute(@event.UserIdPlayer, LogLevel.Information);

        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (match == null || matchData?.current_match_map_id == null)
        {
            return HookResult.Continue;
        }

        IPlayer player = @event.UserIdPlayer;

        _overCapacityKicks.Remove(player.SteamID);

        Guid? lineup_id = MatchUtility.GetPlayerLineup(matchData, player);

        Team placementTeam = match.GetPlacementSide(match.GetExpectedTeam(player));
        int capacity = match.GetExpectedPlayerCount() / 2;

        // Decided before the disconnect timer and the roster resume: the player
        // is about to be kicked, so they must neither count towards a whole
        // roster nor have the kick treated as them leaving the match.
        if (
            LineupCapacityUtility.IsOverCapacity(
                MatchUtility
                    .Players()
                    .Select(connected =>
                        (
                            connected.SteamID.ToString(),
                            MatchUtility.GetPlayerLineup(matchData, connected),
                            (int)connected.Controller.Team
                        )
                    ),
                player.SteamID.ToString(),
                lineup_id,
                (int)placementTeam,
                capacity
            )
        )
        {
            _logger.LogInformation(
                $"Kicking {player.Name} ({player.SteamID}): their lineup already has {capacity} playing"
            );
            _overCapacityKicks.Add(player.SteamID);
            _core.Engine.ExecuteCommand($"kickid {player.UserID}");
            return HookResult.Continue;
        }

        _surrenderSystem.CancelDisconnectTimer(player.SteamID);

        // CancelDisconnectTimer only resumes when that player actually had a
        // timer, which is never the case for someone who left during warmup or
        // knife, or when the pause came from RoundStart going short-handed. If
        // the roster is whole again there is nothing left to wait for.
        _surrenderSystem.ResumeIfRosterWhole();

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
                player.Controller.Clan = role;
                player.Controller.ClanUpdated();
                PendingPlayers.Remove(player.SteamID);
            }

            if (shouldKick && role == null)
            {
                _core.Engine.ExecuteCommand($"kickid {player.UserID}");
                return HookResult.Continue;
            }
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

        Team placementTeam = match.GetPlacementSide(match.GetExpectedTeam(player));

        if (placementTeam != Team.None && joiningTeam != placementTeam)
        {
            return HookResult.Stop;
        }

        return HookResult.Continue;
    }
}
