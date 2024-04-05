using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace FiveStack
{
    public partial class FiveStackPlugin
    {
        // TODO - this is bad , it takes WAY too long
        public void ListenForReadyStatus()
        {
            RegisterListener<Listeners.OnTick>(() =>
            {
                MatchManager? match = _matchService.GetCurrentMatch();

                if (match == null)
                {
                    return;
                }

                if (!match.IsWarmup() && !_gameBackupRounds.IsResttingRound())
                {
                    return;
                }

                for (var i = 1; i <= 10; ++i)
                {
                    CCSPlayerController player = new CCSPlayerController(
                        NativeAPI.GetEntityFromIndex(i)
                    );

                    if (player != null && player.UserId != null && player.IsValid && !player.IsBot)
                    {
                        if (match.IsWarmup())
                        {
                            match.readySystem.SetupReadyMessage(player);
                            continue;
                        }

                        _gameBackupRounds.SetupResetMessage(player);
                    }
                }
            });
        }
    }
}
