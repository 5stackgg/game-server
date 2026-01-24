using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace FiveStack.Services
{
    public class PlayerService : IPlayerService
    {
        public void ChangeTeam(CCSPlayerController player, CsTeam team)
        {
            player.ChangeTeam(team);
        }

        public void Respawn(CCSPlayerController player)
        {
            player.Respawn();
        }

        public void Disconnect(CCSPlayerController player, NetworkDisconnectionReason reason)
        {
            player.Disconnect(reason);
        }

        public void SetPlayerName(CCSPlayerController player, string name)
        {
            if (player == null || player.IsBot)
            {
                return;
            }

            if (player.PlayerName != name)
            {
                player.PlayerName = name;
                CounterStrikeSharp.API.Utilities.SetStateChanged(
                    player,
                    "CBasePlayerController",
                    "m_iszPlayerName"
                );
            }
        }

        public void SetClanTag(CCSPlayerController player, string? tag)
        {
            if (player == null || player.IsBot)
            {
                return;
            }

            if (tag != null)
            {
                tag = $"[{tag.Trim()}]";
            }

            if (player.Clan != tag)
            {
                player.Clan = tag ?? "";

                CounterStrikeSharp.API.Utilities.SetStateChanged(
                    player,
                    "CCSPlayerController",
                    "m_szClan"
                );
                CounterStrikeSharp.API.Utilities.SetStateChanged(
                    player,
                    "CCSPlayerController",
                    "m_szClanName"
                );

                var gameRules = CounterStrikeSharp
                    .API.Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                    .FirstOrDefault();

                if (gameRules is null)
                {
                    return;
                }

                gameRules.GameRules!.NextUpdateTeamClanNamesTime = Server.CurrentTime - 0.01f;
                CounterStrikeSharp.API.Utilities.SetStateChanged(
                    gameRules,
                    "CCSGameRules",
                    "m_fNextUpdateTeamClanNamesTime"
                );
            }
        }

        public void MutePlayer(CCSPlayerController player)
        {
            player.VoiceFlags = VoiceFlags.Muted;
        }

        public void UnmutePlayer(CCSPlayerController player)
        {
            player.VoiceFlags = VoiceFlags.Normal;
        }

        public bool IsCaptain(CCSPlayerController player, CsTeam team)
        {
            // This would be implemented based on the actual captain system logic
            return false;
        }

        public bool IsBot(CCSPlayerController player)
        {
            return player.IsBot;
        }

        public bool IsValid(CCSPlayerController player)
        {
            return player.IsValid;
        }
    }
}