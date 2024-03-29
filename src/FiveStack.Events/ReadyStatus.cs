using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.enums;

namespace FiveStack
{
    public partial class FiveStackPlugin
    {
        public void ListenForReadyStatus()
        {
            RegisterListener<Listeners.OnTick>(() =>
            {
                if (_currentMapStatus != enums.eMapStatus.Warmup)
                {
                    return;
                }

                for (var i = 1; i <= Server.MaxPlayers; ++i)
                {
                    CCSPlayerController player = new CCSPlayerController(
                        NativeAPI.GetEntityFromIndex(i)
                    );

                    if (player != null && player.UserId != null && player.IsValid && !player.IsBot)
                    {
                        int totalReady = TotalReady();
                        int expectedReady = GetExpectedPlayerCount();

                        int playerId = player.UserId.Value;
                        if (_readyPlayers.ContainsKey(playerId) && _readyPlayers[playerId])
                        {
                            player.PrintToCenter(
                                $"Waiting for players [{totalReady}/{expectedReady}]"
                            );
                            continue;
                        }

                        player.PrintToCenter($"Type .r to ready up!");
                    }
                }
            });
        }
    }
}
