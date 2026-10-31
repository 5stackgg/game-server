using CounterStrikeSharp.API.Core;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    private void OnSteamClanChanged(CCSPlayerController player)
    {
        PublishSteamClan(player);
    }

    private void PublishSteamClan(CCSPlayerController player)
    {
        if (!player.IsValid || player.IsBot || player.SteamID == 0)
        {
            return;
        }

        uint? clanId = ClanTagUtility.SteamClans.Get(player.Handle);
        if (clanId == null || clanId == 0)
        {
            return;
        }

        _logger.LogInformation($"{player.SteamID} wears Steam group {clanId} as their clan tag");

        _matchEvents.PublishGameEvent(
            "player-clan-tag",
            new Dictionary<string, object>
            {
                { "steam_id", player.SteamID.ToString() },
                { "clan_id", clanId.Value.ToString() },
            }
        );
    }
}
