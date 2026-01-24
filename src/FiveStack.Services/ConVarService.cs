using CounterStrikeSharp.API.Modules.Cvars;

namespace FiveStack.Services
{
    public class ConVarService : IConVarService
    {
        public ConVar? Find(string name)
        {
            return ConVar.Find(name);
        }

        public void SetValue(string name, string value)
        {
            var convar = ConVar.Find(name);
            convar?.SetValue(value);
        }

        public void SetValue(string name, int value)
        {
            var convar = ConVar.Find(name);
            convar?.SetValue(value);
        }

        public void SetValue(string name, bool value)
        {
            var convar = ConVar.Find(name);
            convar?.SetValue(value);
        }

        public void SetValue(string name, float value)
        {
            var convar = ConVar.Find(name);
            convar?.SetValue(value);
        }
    }
}