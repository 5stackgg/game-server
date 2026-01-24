using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace FiveStack.CounterStrikeSharp.Services
{
    public class ConVarService : IConVarService
    {
        public void SetConVar(CCSPlayerController player, string convarName, string value)
        {
            if (player == null) return;
            player.ExecuteClientCommand($"setconvar {convarName} {value}");
        }

        public string GetConVar(CCSPlayerController player, string convarName)
        {
            if (player == null) return "";
            return player.GetConVar(convarName);
        }

        public void SetGlobalConVar(string convarName, string value)
        {
            Server.ExecuteCommand($"sv_cheats 1; {convarName} {value}; sv_cheats 0");
        }

        public string GetGlobalConVar(string convarName)
        {
            return Server.GetConVar(convarName);
        }
    }
}