using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace FiveStack.CounterStrikeSharp.Services
{
    public class NetworkServices : INetworkServerService
    {
        public void SendToAll(string message)
        {
            Server.PrintToChatAll(message);
        }

        public void SendToPlayer(CCSPlayerController player, string message)
        {
            player.PrintToChat(message);
        }

        public void ExecuteCommand(string command)
        {
            Server.ExecuteCommand(command);
        }
    }
}