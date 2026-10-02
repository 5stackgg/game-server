using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

public partial class FiveStackPlugin
{
    private bool? _communicationAbuseMuteSupported;

    private void ClearCommunicationAbuseMute(IPlayer player, LogLevel logLevel)
    {
        CCSPlayerController controller = player.Controller;

        if (!CommunicationAbuseMuteSupported(controller))
        {
            return;
        }

        try
        {
            if (
                !CommunicationAbuseMute.ShouldClear(
                    controller.HasCommunicationAbuseMute,
                    player.VoiceFlags.HasFlag(VoiceFlagValue.Muted),
                    _matchService.GetCurrentMatch()?.GetMatchData(),
                    player.SteamID.ToString(),
                    player.Name
                )
            )
            {
                return;
            }

            controller.HasCommunicationAbuseMute = false;
            controller.HasCommunicationAbuseMuteUpdated();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(
                ex,
                $"Could not clear Valve's communication abuse mute on {player.SteamID}"
            );
            return;
        }

        _logger.Log(
            logLevel,
            $"Cleared Valve's communication abuse mute on {player.Name} ({player.SteamID})"
        );
    }

    private bool CommunicationAbuseMuteSupported(CCSPlayerController controller)
    {
        if (_communicationAbuseMuteSupported == null)
        {
            try
            {
                _ = controller.HasCommunicationAbuseMute;
                _communicationAbuseMuteSupported = true;
            }
            catch (InvalidOperationException ex)
            {
                _communicationAbuseMuteSupported = false;
                _logger.LogWarning(
                    ex,
                    "CCSPlayerController.m_bHasCommunicationAbuseMute is not available; leaving Valve's report mute to sv_mute_players_with_social_penalties"
                );
            }
        }

        return _communicationAbuseMuteSupported.Value;
    }
}
