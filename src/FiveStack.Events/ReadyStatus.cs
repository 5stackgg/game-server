using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using FiveStack.Entities;

namespace FiveStack
{
    public partial class FiveStackPlugin
    {
        public void ListenForReadyStatus()
        {
            RegisterListener<Listeners.OnTick>(() =>
            {
                FiveStackMatch? match = _matchService.GetMatchData();

                if (match == null)
                {
                    return;
                }

                if (!_matchService.IsWarmup() && !_backUpManagement.IsResttingRound())
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
                        if (_matchService.IsWarmup())
                        {
                            _matchService.readySystem?.SetupReadyMessage(player);
                            continue;
                        }

                        _backUpManagement.SetupResetMessage(match, player);
                    }
                }
            });
        }
    }
}
