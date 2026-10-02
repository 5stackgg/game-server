using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    private const string CommunicationAbuseMuteClass = "CCSPlayerController";
    private const string CommunicationAbuseMuteField = "m_bHasCommunicationAbuseMute";

    private readonly CommunicationAbuseMute _communicationAbuseMute = new();
    private bool? _communicationAbuseMuteSupported;

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
                _communicationAbuseMute.Reasserted(player.SteamID);
            }
        }
    }

    private bool ClearCommunicationAbuseMute(CCSPlayerController player)
    {
        if (!CommunicationAbuseMuteSupported())
        {
            return false;
        }

        try
        {
            if (
                !CommunicationAbuseMute.ShouldClear(
                    player.HasCommunicationAbuseMute,
                    player.VoiceFlags.HasFlag(VoiceFlags.Muted),
                    _matchService.GetCurrentMatch()?.GetMatchData(),
                    player.SteamID.ToString(),
                    player.PlayerName
                )
            )
            {
                return false;
            }

            player.HasCommunicationAbuseMute = false;
            CounterStrikeSharp.API.Utilities.SetStateChanged(
                player,
                CommunicationAbuseMuteClass,
                CommunicationAbuseMuteField
            );
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
            $"Cleared Valve's communication abuse mute on {player.PlayerName} ({player.SteamID})";

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

    // CounterStrikeSharp resolves a missing schema field to offset 0 instead of
    // throwing, so touching the field without this check would write into the
    // controller's vtable.
    private bool CommunicationAbuseMuteSupported()
    {
        if (_communicationAbuseMuteSupported == null)
        {
            _communicationAbuseMuteSupported =
                Schema.IsSchemaFieldNetworked(
                    CommunicationAbuseMuteClass,
                    CommunicationAbuseMuteField
                )
                && Schema.GetSchemaOffset(CommunicationAbuseMuteClass, CommunicationAbuseMuteField)
                    > 0;

            if (_communicationAbuseMuteSupported == false)
            {
                _logger.LogWarning(
                    "CCSPlayerController.m_bHasCommunicationAbuseMute is not available; leaving Valve's report mute to sv_mute_players_with_social_penalties"
                );
            }
        }

        return _communicationAbuseMuteSupported.Value;
    }
}
