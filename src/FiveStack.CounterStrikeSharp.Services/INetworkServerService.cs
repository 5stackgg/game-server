using CounterStrikeSharp.API.Core;

namespace FiveStack.CounterStrikeSharp.Services
{
    public interface INetworkServerService
    {
        void SendToAll(string message);
        void SendToPlayer(CCSPlayerController player, string message);
        void ExecuteCommand(string command);
    }
}