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
                MatchManager? match = CurrentMatch();

                if (match == null)
                {
                    return;
                }

                if (!match.IsWarmup() && !_gameBackupRounds.IsResttingRound())
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
