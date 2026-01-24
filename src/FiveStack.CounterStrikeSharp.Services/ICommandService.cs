namespace FiveStack.CounterStrikeSharp.Services
{
    public interface ICommandService
    {
        void SendCommands(string[] commands);
        void PrintToChat(CCSPlayerController player, string message);
        void PrintToChatAll(string message);
        void PrintToConsole(string message);
        void PrintToCenter(CCSPlayerController player, string message);
        void SendCommand(string command);
    }
}