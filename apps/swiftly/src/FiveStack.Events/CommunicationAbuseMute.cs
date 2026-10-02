using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

public partial class FiveStackPlugin
{
    private readonly CommunicationAbuseMute _communicationAbuseMute = new();
    private bool _communicationAbuseMuteFailed;

    private void WatchCommunicationAbuseMute(IPlayer player)
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

        foreach (IPlayer player in MatchUtility.Players())
        {
            if (due.Contains(player.SteamID) && ClearCommunicationAbuseMute(player))
            {
                _communicationAbuseMute.Watch(player.SteamID);
            }
        }
    }

    private bool ClearCommunicationAbuseMute(IPlayer player)
    {
        if (_communicationAbuseMuteFailed)
        {
            return false;
        }

        try
        {
            CCSPlayerController controller = player.Controller;

            if (
                !CommunicationAbuseMute.ShouldClear(
                    controller.HasCommunicationAbuseMute,
                    player.VoiceFlags.HasFlag(VoiceFlagValue.Muted)
                )
            )
            {
                return false;
            }

            controller.HasCommunicationAbuseMute = false;
            controller.HasCommunicationAbuseMuteUpdated();

            _logger.LogInformation(
                $"Cleared Valve's communication abuse mute on {player.Name} ({player.SteamID})"
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
