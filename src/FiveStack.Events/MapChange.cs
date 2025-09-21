using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using FiveStack.Enums;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    public void ListenForMapChange()
    {
        RegisterListener<Listeners.OnMapStart>(
            async (mapName) =>
            {
                // code smell: we have to wait till server exec's default cfgs
                await Task.Delay(1000 * 5);
                Server.NextFrame(() =>
                {
                    // Reset ready system state when map changes to ensure clean state for new maps
                    MatchManager? match = _matchService.GetCurrentMatch();
                    if (match != null)
                    {
                        _logger.LogInformation("Resetting match state for new map");
                        match.UpdateMapStatus(eMapStatus.Warmup);
                    }

                    _matchService.GetMatchFromApi();
                });
            }
        );
    }
}
