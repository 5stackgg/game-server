using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using PlayCs.entities;

namespace PlayCs;

public partial class PlayCsPlugin
{
    private MatchData? matchData;
    private ConVar password = ConVar.Find("sv_password");

    // RegisterListener<Listeners.OnMapStart>((mapName) =>
    // {
    //     Task.Run(() => SendMessage($"> Changed map to: {mapName}"));
    // });

    private void RegisterAdministrationCommands()
    {
        AddCommandListener("meta", CommandListener_BlockOutput);
        AddCommandListener("css", CommandListener_BlockOutput);
        AddCommandListener("css_plugins", CommandListener_BlockOutput);

        AddCommand("update_phase", "updates the match phase", ServerUpdatePhase);
        AddCommand(
            "match_details",
            "gets the list of players currently in the server",
            SetMatchDetails
        );
    }

    public void SetMatchDetails(CCSPlayerController? player, CommandInfo command)
    {
        matchData = JsonSerializer.Deserialize<MatchData>(command.ArgString);

        if (matchData == null)
        {
            return;
        }

        // Console.WriteLine($"SET PASSSWORD {matchData.password}");
        // we cant detect it has changed, its encrypted
        // password.StringValue = matchData.password;
        // password.StringValue = "connectme";

        if (matchData.map != CurrentMap)
        {
            Console.WriteLine($"Change Level {matchData.map}");
            ChangeMap(matchData.map);
        }

        if (PhaseStringToEnum(matchData.status) != CurrentPhase)
        {
            UpdatePhase(PhaseStringToEnum(matchData.status));
        }
    }

    public void UpdatePhase(ePhase phase)
    {
        Console.WriteLine($"Updating Phase: {phase}");

        switch (phase)
        {
            case ePhase.Scheduled:
                break;
            case ePhase.Warmup:
                startWarmup();
                break;
            case ePhase.Knife:
                if (matchData!.knife_round)
                {
                    startKnife();
                }
                else
                {
                    UpdatePhase(ePhase.Live);
                }
                break;
            case ePhase.Live:
                startLive();
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

        Eventing.PublishMatchEvent(
            matchData!.id,
            new Eventing.EventData<Dictionary<string, object>>
            {
                @event = "status",
                data = new Dictionary<string, object> { { "status", phase.ToString() }, }
            }
        );

        CurrentPhase = phase;
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
