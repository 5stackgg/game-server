using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    private readonly CommunicationAbuseMute _communicationAbuseMute = new();
    private bool _communicationAbuseMuteFailed;

    private void WatchCommunicationAbuseMute(CCSPlayerController player)
    {
        ClearCommunicationAbuseMute(player);
        _communicationAbuseMute.Watch(player.SteamID);
    }

    private void RecheckCommunicationAbuseMutes()
    {
        List<ulong> due = _communicationAbuseMute.Due();

        if (due.Count == 0)
        {
            return;
        }

        foreach (CCSPlayerController player in MatchUtility.Players())
        {
            if (due.Contains(player.SteamID) && ClearCommunicationAbuseMute(player))
            {
                _communicationAbuseMute.Watch(player.SteamID);
            }
        }
    }

    private bool ClearCommunicationAbuseMute(CCSPlayerController player)
    {
        if (_communicationAbuseMuteFailed)
        {
            return false;
        }

        try
        {
            if (
                !CommunicationAbuseMute.ShouldClear(
                    player.HasCommunicationAbuseMute,
                    player.VoiceFlags.HasFlag(VoiceFlags.Muted)
                )
            )
            {
                return false;
            }

            player.HasCommunicationAbuseMute = false;
            CounterStrikeSharp.API.Utilities.SetStateChanged(
                player,
                "CCSPlayerController",
                "m_bHasCommunicationAbuseMute"
            );

            _logger.LogInformation(
                $"Cleared Valve's communication abuse mute on {player.PlayerName} ({player.SteamID})"
            );

            return true;
        }
        catch (Exception ex)
        {
            _communicationAbuseMuteFailed = true;
            _logger.LogError(
                ex,
                "Could not clear Valve's communication abuse mute; leaving it alone until the plugin reloads"
            );
            return false;
        }
    }
}
