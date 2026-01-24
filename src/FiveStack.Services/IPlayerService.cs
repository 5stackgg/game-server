using CounterStrikeSharp.API.Core;

namespace FiveStack.Services
{
    public interface IPlayerService
    {
        void ChangeTeam(CCSPlayerController player, CsTeam team);
        void Respawn(CCSPlayerController player);
        void Disconnect(CCSPlayerController player, NetworkDisconnectionReason reason);
        void SetPlayerName(CCSPlayerController player, string name);
        void SetClanTag(CCSPlayerController player, string? tag);
        void MutePlayer(CCSPlayerController player);
        void UnmutePlayer(CCSPlayerController player);
        bool IsCaptain(CCSPlayerController player, CsTeam team);
        bool IsBot(CCSPlayerController player);
        bool IsValid(CCSPlayerController player);
    }
}