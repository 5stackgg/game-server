using CounterStrikeSharp.API.Core;

namespace FiveStack.CounterStrikeSharp.Services
{
    public interface IConVarService
    {
        void SetConVar(CCSPlayerController player, string convarName, string value);
        string GetConVar(CCSPlayerController player, string convarName);
        void SetGlobalConVar(string convarName, string value);
        string GetGlobalConVar(string convarName);
    }
}