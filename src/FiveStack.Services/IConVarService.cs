using CounterStrikeSharp.API.Modules.Cvars;

namespace FiveStack.Services
{
    public interface IConVarService
    {
        ConVar? Find(string name);
        void SetValue(string name, string value);
        void SetValue(string name, int value);
        void SetValue(string name, bool value);
        void SetValue(string name, float value);
    }
}