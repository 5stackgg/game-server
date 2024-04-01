using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.entities;
using FiveStack.enums;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("get_match", "Gets match information from api")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void get_match(CCSPlayerController? player, CommandInfo command)
    {
        GetMatch();
    }

    private async void GetMatch()
    {
        HttpClient httpClient = new HttpClient();

        string? serverId = Environment.GetEnvironmentVariable("SERVER_ID");
        string? apiPassword = Environment.GetEnvironmentVariable("SERVER_API_PASSWORD");

        Logger.LogInformation($"Server ID: {serverId}");

        if (serverId == null || apiPassword == null)
        {
            Logger.LogWarning("Missing Server ID / API Password");
            await Task.Delay(1000 * 5);
            Server.NextFrame(() =>
            {
                GetMatch();
            });

            return;
        }

        try
        {
            Logger.LogInformation(
                $"Fetching Match Info: https://api.5stack.gg/server/match/{serverId}"
            );

            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiPassword);

            string? response = await httpClient.GetStringAsync(
                $"https://api.5stack.gg/server/match/{serverId}"
            );

            Server.NextFrame(() =>
            {
                if (response.Length == 0)
                {
                    Logger.LogWarning("currenlty no match assigned to server");
                    return;
                }

                _matchData = JsonSerializer.Deserialize<Match>(response);

                if (_matchData == null)
                {
                    return;
                }

                if (
                    serverId != null
                    && apiPassword != null
                    && (_redis.IsConnected() == false || _currentMatchId != _matchData.id)
                )
                {
                    _redis.Connect(serverId, apiPassword);
                }

                if (IsWarmup())
                {
                    Message(HudDestination.Alert, "Received Match Data");
                }

                SetupMatch();
            });
        }
        catch (HttpRequestException ex)
        {
            Logger.LogInformation($"HTTP request error: {ex.Message}");
        }
        catch (JsonException ex)
        {
            Logger.LogInformation($"JSON deserialization error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger.LogInformation($"An unexpected error occurred: {ex.Message}");
        }
    }

    [ConsoleCommand("match_state", "Forces a match to update its current state")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void SetMatchState(CCSPlayerController? player, CommandInfo command)
    {
        UpdateMapStatus(MapStatusStringToEnum(command.ArgString));
    }

    [ConsoleCommand("css_reset", "Restores to a previous round")]
    public void RestoreRound(CCSPlayerController? player, CommandInfo command)
    {
        // TODO - THINGS TO THINK ABOUT - timeouts
        // TODO - stats that were recorded need to be erased
        // TODO -  so we need an evet when restoring rounds
        if (_matchData == null)
        {
            return;
        }

        if (_resetRound != null)
        {
            if (
                player != null
                && player.UserId != null
                && GetMemberFromLineup(player)?.captain == true
            )
            {
                _restoreRoundVote[player.UserId.Value] = true;
            }
            return;
        }

        string round = command.ArgByIndex(1);

        if (RestoreBackupRound(round, player != null) == false)
        {
            command.ReplyToCommand($"Unable to restore round, missing file");
            return;
        }

        if (player != null && player.UserId != null && GetMemberFromLineup(player)?.captain == true)
        {
            _restoreRoundVote[player.UserId.Value] = true;
        }
    }

    public void UpdateMapStatus(eMapStatus status)
    {
        if (_matchData == null)
        {
            Logger.LogInformation("missing event data");
            return;
        }

        Logger.LogInformation($"Update Map Status {_currentMapStatus} -> {status}");

        switch (status)
        {
            case eMapStatus.Scheduled:
            case eMapStatus.Warmup:
                status = eMapStatus.Warmup;
                StartWarmup();
                break;
            case eMapStatus.Knife:
                if (!_matchData.knife_round)
                {
                    UpdateMapStatus(eMapStatus.Live);
                    break;
                }

                var currentMap = GetCurrentMap();
                if (currentMap == null)
                {
                    break;
                }

                if (currentMap.order == _matchData.best_of && _matchData.knife_round)
                {
                    StartKnife();
                }

                break;
            case eMapStatus.Live:
                StartLive();
                break;
            default:
                PublishMapStatus(status);
                break;
        }

        _currentMapStatus = status;
    }

    public void PublishMapStatus(eMapStatus status)
    {
        PublishGameEvent(
            "mapStatus",
            new Dictionary<string, object> { { "status", status.ToString() }, }
        );
    }

    public void SetupMatch()
    {
        if (_matchData == null)
        {
            Logger.LogInformation("Missing Match Data");
            return;
        }
        Logger.LogInformation($"Setup Match {_matchData.id}");

        _currentMatchId = _matchData.id;

        _currentMap = GetCurrentMap();

        if (_currentMap == null)
        {
            Logger.LogWarning("Unable to find map");
            return;
        }

        if (!IsOnMap(_currentMap.map.name))
        {
            ChangeMap(_currentMap.map);
            return;
        }

        SendCommands(new[] { $"sv_password \"{_matchData.password}\"" });

        SetupTeamNames();

        Logger.LogInformation(
            $"Current Game State {_currentMapStatus}:{_currentMap.status}:{_currentMap.map.name}"
        );

        if (MapStatusStringToEnum(_currentMap.status) != _currentMapStatus)
        {
            UpdateMapStatus(MapStatusStringToEnum(_currentMap.status));
        }
    }

    public MatchMap? GetCurrentMap()
    {
        if (_matchData == null || _matchData.current_match_map_id == null)
        {
            return null;
        }

        return _matchData?.match_maps.FirstOrDefault(match_map =>
        {
            return match_map.id == _matchData.current_match_map_id;
        });
    }

    public void SetupTeamNames()
    {
        if (_matchData == null)
        {
            return;
        }

        if (_matchData.lineup_1.name != null)
        {
            SendCommands(new[] { $"mp_teamname_1 {_matchData.lineup_1.name}" });
        }

        if (_matchData.lineup_2.name != null)
        {
            SendCommands(new[] { $"mp_teamname_2 {_matchData.lineup_2.name}" });
        }
    }

    public void ChangeMap(Map map)
    {
        Logger.LogInformation($"Changing Map {map.name}");

        if (map.workshop_map_id == null && Server.IsMapValid(map.name))
        {
            SendCommands(new[] { $"changelevel \"{map.name}\"" });
        }
        else
        {
            SendCommands(new[] { $"host_workshop_map {map.workshop_map_id}" });
        }
    }

    public bool IsOnMap(string map)
    {
        Logger.LogInformation($"Map Check: {_onMap}:{map}");

        return map == _onMap;
    }

    public Guid? GetPlayerLineup(CCSPlayerController player)
    {
        MatchMember? member = GetMemberFromLineup(player);

        if (member == null)
        {
            Logger.LogInformation($"Unable to find player {player.SteamID.ToString()}");
            return null;
        }

        return member.match_lineup_id;
    }

    public MatchMember? GetMemberFromLineup(CCSPlayerController player)
    {
        if (_matchData == null)
        {
            return null;
        }

        List<MatchMember> players = _matchData
            .lineup_1.lineup_players.Concat(_matchData.lineup_2.lineup_players)
            .ToList();

        return players.Find(member =>
        {
            if (member.steam_id == null)
            {
                return member.name.StartsWith(player.PlayerName);
            }

            return member.steam_id == player.SteamID.ToString();
        });
    }

    private bool RestoreBackupRound(string round, bool byVote = true)
    {
        string backupRoundFile = $"{GetSafeMatchPrefix()}_round{round.PadLeft(2, '0')}.txt";

        if (!File.Exists(Path.Join(Server.GameDirectory + "/csgo/", backupRoundFile)))
        {
            return false;
        }

        SendCommands(new[] { "mp_pause_match" });

        if (byVote)
        {
            _resetRound = round;

            Message(
                HudDestination.Alert,
                $" {ChatColors.Red}Reset round to {round}, captains must accept"
            );
            return true;
        }

        SendCommands(new[] { $"mp_backup_restore_load_file {backupRoundFile}" });

        Message(
            HudDestination.Alert,
            $" {ChatColors.Red}Round {round} has been restored (.resume to continue)"
        );

        return true;
    }
}
