using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using PlayCs.entities;
using PlayCS.enums;

namespace PlayCs;

public partial class PlayCsPlugin
{
    private Match? _matchData;

    [ConsoleCommand("set_match_id", "Set the match id for the server to configure the match for")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void SetMatchMatchId(CCSPlayerController? player, CommandInfo command)
    {
        string matchId = command.ArgString;
        _matchData = _redis.GetMatch(matchId);

        if (_matchData == null)
        {
            return;
        }

        if (!IsLive())
        {
            Message(HudDestination.Alert, "Received Match Data");
        }

        SetupMatch();
    }

    [ConsoleCommand("match_state", "Forces a match to update its current state")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void SetMatchState(CCSPlayerController? player, CommandInfo command)
    {
        UpdateGameState(GameStateStringToEnum(command.ArgString));
    }

    [ConsoleCommand("restore_round", "Restores to a previous round")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void RestoreRound(CCSPlayerController? player, CommandInfo command)
    {
        if (_matchData == null)
        {
            return;
        }

        string round = command.ArgByIndex(1);
        string backupRoundFile =
            $"{GetSafeMatchPrefix()}_round{round.ToString().PadLeft(2, '0')}.txt";

        if (!File.Exists(Path.Join(Server.GameDirectory + "/csgo/", backupRoundFile)))
        {
            command.ReplyToCommand($"Unable to restore round, missing file ({backupRoundFile})");
            return;
        }

        SendCommands(new[] { "mp_pause_match", $"mp_backup_restore_load_file {backupRoundFile}" });

        Message(
            HudDestination.Alert,
            $" {ChatColors.Red}Round {round} has been restored (.resume to continue)"
        );
    }

    public void UpdateGameState(eGameState gameState)
    {
        Console.WriteLine($"Update Game State {_currentGameState} -> {gameState}");
        if (_matchData == null)
        {
            Console.WriteLine("missing event data");
            return;
        }
        Console.WriteLine($"Updating Game State: {gameState}");

        switch (gameState)
        {
            case eGameState.Scheduled:
                break;
            case eGameState.Warmup:
                StartWarmup();
                break;
            case eGameState.Knife:
                if (!_matchData.knife_round)
                {
                    UpdateGameState(eGameState.Live);
                    break;
                }
                StartKnife();
                break;
            case eGameState.Live:
                StartLive();
                break;
            case eGameState.Overtime:
            case eGameState.Paused:
            case eGameState.TechTimeout:
            case eGameState.Finished:
                _publishGameState(gameState);
                break;
        }

        _currentGameState = gameState;
    }

    private void _publishGameState(eGameState gameState)
    {
        if (_matchData == null)
        {
            return;
        }

        _redis.PublishMatchEvent(
            _matchData.id,
            new Redis.EventData<Dictionary<string, object>>
            {
                @event = "status",
                data = new Dictionary<string, object> { { "status", gameState.ToString() }, }
            }
        );
    }

    public async void SetupMatch()
    {
        if (_matchData == null)
        {
            Console.WriteLine("Missing Match Data");
            return;
        }
        Console.WriteLine($"Setup Match ${_matchData.id}");

        if (!IsOnMap(_matchData.map))
        {
            Console.WriteLine($"Changing Map {_matchData.map}");
            await ChangeMap(_matchData.map);
            return;
        }

        SendCommands(new[] { $"sv_password \"{_matchData.password}\"" });

        SetupTeamNames();

        Console.WriteLine($"Current Game State {_currentGameState}");

        if (GameStateStringToEnum(_matchData.status) != _currentGameState)
        {
            UpdateGameState(GameStateStringToEnum(_matchData.status));
        }
    }

    public void SetupTeamNames()
    {
        if (_matchData == null)
        {
            return;
        }

        foreach (var team in _matchData.teams)
        {
            SendCommands(new[] { $"mp_teamname_{team.team_number} {team.name}" });
        }
    }

    private Dictionary<string, string> _workshopMaps = new Dictionary<string, string>
    {
        { "de_cache", "3070596702" },
        { "de_cbble", "3070212801" },
        { "de_train", "3070284539" }
    };

    public async Task ChangeMap(string map)
    {
        if (Server.IsMapValid(map) && !_workshopMaps.ContainsKey(map))
        {
            SendCommands(new[] { $"changelevel \"{map}\"" });
        }
        else
        {
            if (!_workshopMaps.ContainsKey(map))
            {
                // dont want to break the server by changing it forever
                UpdateGameState(eGameState.Scheduled);
                _matchData = null;
                return;
            }
            SendCommands(new[] { $"host_workshop_map {_workshopMaps[map]}" });
        }

        // give the server some time to change, if the map didnt change we will try again.
        await Task.Delay(1000 * 5);

        if (!IsOnMap(map))
        {
            await ChangeMap(map);
        }
    }

    public bool IsOnMap(string map)
    {
        Console.WriteLine($"Map Check: {_currentMap}:{map == _currentMap}");

        return map == _currentMap;
    }
}
