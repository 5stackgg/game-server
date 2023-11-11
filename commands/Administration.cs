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
    public void SetMatchMatchId(CCSPlayerController? player, CommandInfo command)
    {
        string matchId = command.ArgString;
        _matchData = _redis.GetMatch(matchId);

        if (_matchData == null)
        {
            return;
        }

        Message(HudDestination.Alert, "Received Match Data");

        SetupMatch();
    }

    [ConsoleCommand("match_phase", "Forces a match to update its current phase")]
    public void SetMatchPhase(CCSPlayerController? player, CommandInfo command)
    {
        UpdatePhase(PhaseStringToEnum(command.ArgString));
    }

    public void UpdatePhase(ePhase phase)
    {
        Console.WriteLine($"Update Phase {_currentPhase} -> {phase}");
        if (_matchData == null)
        {
            Console.WriteLine("missing event data");
            return;
        }
        Console.WriteLine($"Updating Phase: {phase}");

        switch (phase)
        {
            case ePhase.Scheduled:
                break;
            case ePhase.Warmup:
                StartWarmup();
                break;
            case ePhase.Knife:
                if (!_matchData.knife_round)
                {
                    UpdatePhase(ePhase.Live);
                    break;
                }
                StartKnife();
                break;
            case ePhase.Live:
                StartLive();
                break;
            case ePhase.Overtime:
            case ePhase.Paused:
            case ePhase.TechTimeout:
            case ePhase.Finished:
                _publishPhase(phase);
                break;
        }

        _currentPhase = phase;
    }

    private void _publishPhase(ePhase phase)
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
                data = new Dictionary<string, object> { { "status", phase.ToString() }, }
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

        if (_matchData.map != _currentMap)
        {
            Console.WriteLine($"Changing Map {_matchData.map}");
            await ChangeMap(_matchData.map);
            return;
        }

        SendCommands(new[] { $"sv_password \"{_matchData.password}\"" });

        SetupTeamNames();

        UpdateCurrentRound();

        Console.WriteLine($"Current Phase {_currentPhase}");

        if (PhaseStringToEnum(_matchData.status) != _currentPhase)
        {
            UpdatePhase(PhaseStringToEnum(_matchData.status));
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

    public async Task ChangeMap(string map)
    {
        string changeCommand = Server.IsMapValid(map) ? "changelevel" : "host_workshop_map";

        SendCommands(new[] { $"{changeCommand} \"{map}\"" });

        // give the server some time to change, if the map didnt change we will try again.
        await Task.Delay(1000 * 5);

        if (_currentMap != map)
        {
            await ChangeMap(map);
        }
    }
}
