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
                if (!IsWarmup() && _resetRound == null)
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
                        if (IsWarmup())
                        {
                            SetupReadyMessage(player);
                            continue;
                        }

                        SetupResetMessage(player);
                    }
                }
            });
        }

        public void SetupReadyMessage(CCSPlayerController player)
        {
            if (player.UserId == null)
            {
                return;
            }

            int totalReady = TotalReady();
            int expectedReady = GetExpectedPlayerCount();

            int playerId = player.UserId.Value;
            if (_readyPlayers.ContainsKey(playerId) && _readyPlayers[playerId])
            {
                player.PrintToCenter($"Waiting for players [{totalReady}/{expectedReady}]");
                return;
            }
            player.PrintToCenter($"Type .r to ready up!");
        }

        public void SetupResetMessage(CCSPlayerController player)
        {
            if (player.UserId == null)
            {
                return;
            }

            int totalVoted = _restoreRoundVote.Count(pair => pair.Value);

            int playerId = player.UserId.Value;
            bool isCaptain = GetMemberFromLineup(player)?.captain ?? false;

            if (
                isCaptain == false
                || _restoreRoundVote.ContainsKey(playerId) && _restoreRoundVote[playerId]
            )
            {
                player.PrintToCenter($"Waiting for captin [{totalVoted}/2]");
                return;
            }

            player.PrintToCenter($"Type .reset reset the round to round {_resetRound}");
        }
    }
}
