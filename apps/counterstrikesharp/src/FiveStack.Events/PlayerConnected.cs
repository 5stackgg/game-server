using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    private readonly HashSet<ulong> _overCapacityKicks = new();

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

        CCSPlayerController player = @event.Userid;

        _overCapacityKicks.Remove(player.SteamID);

        Guid? lineup_id = MatchUtility.GetPlayerLineup(matchData, player);

        CsTeam placementTeam = match.GetPlacementSide(match.GetExpectedTeam(player));
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
                            (int)connected.Team
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
                $"Kicking {player.PlayerName} ({player.SteamID}): their lineup already has {capacity} playing"
            );
            _overCapacityKicks.Add(player.SteamID);
            Server.ExecuteCommand($"kickid {player.UserId}");
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

        if (shouldKick && lineup_id == null)
        {
            string? role = null;
            if (PendingPlayers.ContainsKey(player.SteamID))
            {
                role = PendingPlayers[player.SteamID];
                player.Clan = role;
                PendingPlayers.Remove(player.SteamID);
            }

            if (role == null)
            {
                Server.ExecuteCommand($"kickid {player.UserId}");
                return HookResult.Continue;
            }
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
            _gameServer.SendCommands(["mp_warmup_start"]);
        }

        CCSPlayerController player = @event.Userid;

        if (_readySystem.IsWaitingForReady())
        {
            _gameServer.Message(
                HudDestination.Chat,
                Localizer[
                    "player.join.ready_hint",
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

        if (!int.TryParse(info.ArgByIndex(1), out int joiningTeamNum))
        {
            // Team number comes from a client command argument; a non-numeric
            // value must not throw out of the hook.
            return HookResult.Continue;
        }

        CsTeam joiningTeam = TeamUtility.TeamNumToCSTeam(joiningTeamNum);

        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null)
        {
            return HookResult.Continue;
        }

        CsTeam placementTeam = match.GetPlacementSide(match.GetExpectedTeam(player));

        if (placementTeam != CsTeam.None && joiningTeam != placementTeam)
        {
            return HookResult.Stop;
        }

        return HookResult.Continue;
    }
}
