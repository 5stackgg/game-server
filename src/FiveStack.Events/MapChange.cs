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
                _logger.LogInformation("Precaching models for new map");

                Server.PrecacheModel(ModelPathCtmSas);
                Server.PrecacheModel(ModelPathTmPhoenix);

                // code smell: we have to wait till server exec's default cfgs
                await Task.Delay(1000 * 5);
                Server.NextFrame(() =>
                {
                    _matchService.GetCurrentMatch()?.reset();
                    _matchService.GetMatchFromApi();
                });
            }
        );
    }
}
