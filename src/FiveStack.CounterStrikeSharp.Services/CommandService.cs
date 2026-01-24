using CounterStrikeSharp.API.Core;

namespace FiveStack.CounterStrikeSharp.Services
{
    public class CommandService : ICommandService
    {
        public void SendCommands(string[] commands)
        {
            foreach (var command in commands)
            {
                Server.ExecuteCommand(command);
            }
        }

        public void PrintToChat(CCSPlayerController player, string message)
        {
            player.PrintToChat(message);
        }

        public void PrintToChatAll(string message)
        {
            Server.PrintToChatAll(message);
        }

        public void PrintToConsole(string message)
        {
            Server.PrintToConsole(message);
        }

        public void PrintToCenter(CCSPlayerController player, string message)
        {
            player.PrintToCenter(message);
        }

        public void SendCommand(string command)
        {
            Server.ExecuteCommand(command);
        }
    }
}