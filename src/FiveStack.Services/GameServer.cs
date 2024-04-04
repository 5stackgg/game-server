using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

namespace FiveStack;

public class GameServer
{
    public GameServer() { }

    public void SendCommands(string[] commands)
    {
        foreach (var command in commands)
        {
            Server.ExecuteCommand(command);
        }
    }

    public void Message(
        HudDestination destination,
        string message,
        CCSPlayerController? player = null
    )
    {
        if (player != null)
        {
            var parts = message.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var part in parts)
            {
                player.PrintToChat($"{part}");
            }
        }
        else if (destination == HudDestination.Console)
        {
            Server.PrintToConsole(message);
        }
        else if (destination == HudDestination.Alert || destination == HudDestination.Center)
        {
            VirtualFunctions.ClientPrintAll(destination, $" {message}", 0, 0, 0, 0);
        }
        else
        {
            var parts = message.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var part in parts)
            {
                Server.PrintToChatAll($"{part}");
            }
        }
    }

    public int GetCurrentRound()
    {
        int roundsPlayed = 0;
        var teamManagers = CounterStrikeSharp.API.Utilities.FindAllEntitiesByDesignerName<CCSTeam>(
            "cs_team_manager"
        );

        foreach (var teamManager in teamManagers)
        {
            if (
                teamManager.TeamNum == (int)CsTeam.Terrorist
                || teamManager.TeamNum == (int)CsTeam.CounterTerrorist
            )
            {
                roundsPlayed += teamManager.Score;
            }
        }

        return roundsPlayed + 1;
    }
}
