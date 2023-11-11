using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using PlayCs.entities;
using PlayCS.enums;

namespace PlayCs;

public partial class PlayCsPlugin
{
    private Match? _matchData;

    private void RegisterAdministrationCommands()
    {
        AddCommandListener("meta", CommandListener_BlockOutput);
        AddCommandListener("css", CommandListener_BlockOutput);
        AddCommandListener("css_plugins", CommandListener_BlockOutput);

        AddCommand("update_phase", "updates the match phase", ServerUpdatePhase);
        AddCommand("set_match_id", "sets match id", SetMatchMatchId);
    }

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

    public void SetupMatch()
    {
        if (_matchData == null)
        {
            return;
        }

        if (_matchData.map != _currentMap)
        {
            Console.WriteLine($"Changing Map {_matchData.map}");
            ChangeMap(_matchData.map);
            return;
        }

        Console.WriteLine($"Setup Match {_matchData.id}");

        SendCommands(new[] { $"sv_password \"{_matchData.password}\"" });

        SetupTeamNames();

        UpdateCurrentRound();

        if (PhaseStringToEnum(_matchData.status) != _currentPhase)
        {
            UpdatePhase(PhaseStringToEnum(_matchData.status));
        }
    }

    private void SetupTeamNames()
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

    public void UpdatePhase(ePhase phase)
    {
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
                if (_matchData!.knife_round)
                {
                    StartKnife();
                }
                else
                {
                    UpdatePhase(ePhase.Live);
                }
                break;
            case ePhase.Live:
                StartLive();
                break;
            case ePhase.Overtime:
                Console.WriteLine("Overtime phase");
                break;
            case ePhase.Paused:
            case ePhase.TechTimeout:
                break;
            case ePhase.Finished:
                Console.WriteLine("Finished phase");
                break;
        }

        _redis.PublishMatchEvent(
            _matchData.id,
            new Redis.EventData<Dictionary<string, object>>
            {
                @event = "status",
                data = new Dictionary<string, object> { { "status", phase.ToString() }, }
            }
        );

        _currentPhase = phase;
    }

    public void ChangeMap(string map)
    {
        if (Server.IsMapValid(map))
        {
            SendCommands(new[] { $"changelevel \"{map}\"" });
            return;
        }

        // TODO - check if map exist in subscribed map list
        SendCommands(new[] { $"host_workshop_map \"{map}\"" });
    }

    private void ServerUpdatePhase(CCSPlayerController? player, CommandInfo command)
    {
        UpdatePhase(PhaseStringToEnum(command.ArgString));
    }

    public HookResult CommandListener_BlockOutput(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return HookResult.Continue;
        }

        return HookResult.Stop;
    }
}
