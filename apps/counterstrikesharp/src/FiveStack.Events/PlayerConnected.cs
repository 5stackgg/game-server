using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.ValveConstants.Protobuf;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    private readonly HashSet<ulong> _overCapacityKicks = new();
    private readonly HashSet<ulong> _refusedKicks = new();
    private const float RefusedKickDelaySeconds = 4f;

    [GameEventHandler]
    public HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
    {
        if (@event.Userid == null || !@event.Userid.IsValid || @event.Userid.IsBot)
        {
            return HookResult.Continue;
        }

        // Still marked after a map change inside the kick delay: kicked again.
        if (
            RefusedConnects.Take(@event.Userid.SteamID)
            || _refusedKicks.Contains(@event.Userid.SteamID)
        )
        {
            CCSPlayerController refused = @event.Userid;
            ulong steamId = refused.SteamID;
            string reason = _gameServer.Localize(refused, "player.join.refused");

            _logger.LogInformation($"Kicking {refused.PlayerName} ({steamId}): refused at connect");
            _refusedKicks.Add(steamId);

            // CounterStrikeSharp cannot put a reason on the kick, so it is
            // shown first.
            refused.ChangeTeam(CsTeam.Spectator);
            refused.PrintToChat(reason);
            refused.PrintToCenter(reason);
            TimerUtility.AddTimer(
                RefusedKickDelaySeconds,
                () =>
                {
                    // Gone and back in the meantime: that connect decides again.
                    if (
                        refused.IsValid
                        && refused.SteamID == steamId
                        && refused.Connected == PlayerConnectedState.Connected
                        && _refusedKicks.Contains(steamId)
                    )
                    {
                        refused.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);
                    }
                }
            );
            return HookResult.Continue;
        }

        ClearCommunicationAbuseMute(@event.Userid, LogLevel.Information);

        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (match == null || matchData?.current_match_map_id == null)
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
        // timer, which outside a LAN tournament is never the case for someone
        // who left during warmup or knife, or when the pause came from
        // RoundStart going short-handed. If the roster is whole again there is
        // nothing left to wait for.
        _surrenderSystem.ResumeIfRosterWhole();
        Server.NextFrame(() => _gameBackupRounds.OfferPendingRestore());

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
            string? role = ConnectRoles.Claim(player.SteamID, matchData.id);

            if (role != null)
            {
                ClanTagUtility.Set(player, role);
            }

            if (shouldKick && role == null)
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

        if (
            LineupMembership.WaitsForADiscordLink(
                matchData,
                player.SteamID.ToString(),
                ConnectRoles.RoleIn(player.SteamID, matchData.id)
            )
        )
        {
            _gameServer.Message(
                HudDestination.Chat,
                _gameServer.Localize(player, "player.join.link_discord"),
                player
            );
        }

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

        if (match.readySystem.IsWaitingForReady())
        {
            _gameServer.Message(
                HudDestination.Chat,
                _gameServer.Localize(
                    player,
                    "player.join.ready_hint",
                    ChatColors.Green,
                    CommandUtility.PublicChatTrigger,
                    ChatColors.Default
                ),
                player
            );
        }

        _gameServer.Message(
            HudDestination.Chat,
            _gameServer.Localize(
                player,
                "player.join.help_hint",
                ChatColors.Green,
                CommandUtility.SilentChatTrigger,
                ChatColors.Default
            ),
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
