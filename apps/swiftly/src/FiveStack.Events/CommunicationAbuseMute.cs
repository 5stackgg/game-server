using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

public partial class FiveStackPlugin
{
    private readonly CommunicationAbuseMute _communicationAbuseMute = new();
    private bool? _communicationAbuseMuteSupported;

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
                _communicationAbuseMute.Reasserted(player.SteamID);
            }
        }
    }

    private bool ClearCommunicationAbuseMute(IPlayer player)
    {
        CCSPlayerController controller = player.Controller;

        if (!CommunicationAbuseMuteSupported(controller))
        {
            return false;
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
                return false;
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
            return false;
        }

        string message =
            $"Cleared Valve's communication abuse mute on {player.Name} ({player.SteamID})";

        if (_communicationAbuseMute.FirstClear(player.SteamID))
        {
            _logger.LogInformation(message);
        }
        else
        {
            _logger.LogDebug(message);
        }

        return true;
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
