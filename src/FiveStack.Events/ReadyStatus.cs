using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

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
                    var player = new CCSPlayerController(NativeAPI.GetEntityFromIndex(i));

                    if (player.IsValid && !player.IsBot) // Simplified the condition
                    {
                        int totalReady = TotalReady();
                        int expectedReady = GetExpectedPlayerCount();

                        player.PrintToCenter(
                            $"Waiting for Players [Ready {totalReady}/{expectedReady}] Type .r to Ready Up!"
                        );
                    }
                }
            });
        }
    }
}
