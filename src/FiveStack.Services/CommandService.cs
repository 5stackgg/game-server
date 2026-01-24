using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace FiveStack.Services
{
    public class CommandService : ICommandService
    {
        public void SendCommands(string[] commands)
        {
            Server.NextFrame(() => Server.ExecuteCommand(string.Join(";", commands)));
        }

        public void PrintToChat(CCSPlayerController player, string message)
        {
            var parts = message.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
            foreach (var part in parts)
            {
                player.PrintToChat($"{part}");
            }
        }

        public void PrintToChatAll(string message)
        {
            var parts = message.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
            foreach (var part in parts)
            {
                Server.PrintToChatAll($"{part}");
            }
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
            Server.NextFrame(() => Server.ExecuteCommand(command));
        }
    }
}