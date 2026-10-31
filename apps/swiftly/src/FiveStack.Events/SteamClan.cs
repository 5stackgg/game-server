using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Players;

namespace FiveStack;

public partial class FiveStackPlugin
{
    private void OnSteamClanChanged(nint controller)
    {
        IPlayer? player = Core
            .PlayerManager.GetAllPlayers()
            .FirstOrDefault(player => player.Controller.Address == controller);

        if (player != null)
        {
            PublishSteamClan(player);
        }
    }

    private void PublishSteamClan(IPlayer player)
    {
        if (!player.IsValid || player.IsFakeClient || player.SteamID == 0)
        {
            return;
        }

        uint? clanId = ClanTagUtility.SteamClans.Get(player.Controller.Address);
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
