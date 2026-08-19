using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Entities;

namespace UtilityPractice;

public partial class UtilityPracticePlugin
{
    // Practising smokes through your own flash is nobody's idea of practice.
    [GameEventHandler]
    public HookResult OnPlayerBlind(EventPlayerBlind @event, GameEventInfo info)
    {
        if (!_config.NoFlash)
        {
            return HookResult.Continue;
        }

        CCSPlayerPawn? pawn = @event.Userid?.PlayerPawn.Value;

        if (pawn == null || !pawn.IsValid)
        {
            return HookResult.Continue;
        }

        pawn.FlashDuration = 0f;

        return HookResult.Continue;
    }

    private void OnClientAuthorized(int slot, SteamID steamId)
    {
        _library.Refresh(steamId.SteamId64);
    }

    private void OnClientDisconnect(int slot)
    {
        CCSPlayerController? player = Utilities.GetPlayerFromSlot(slot);

        if (player == null || !player.IsValid)
        {
            return;
        }

        // A run ends with the player who was in it: the map is still standing,
        // but nobody is left to be teleported or told anything.
        _drill.Forget(player.SteamID);
        _system.Forget(player.SteamID);
    }

    // A preview belongs to whoever asked for it. Solo is the default, so the
    // usual case is that nobody else is sent the beams at all.
    private void OnCheckTransmit(CCheckTransmitInfoList infoList)
    {
        // This runs every frame, so the common case -- nobody previewing
        // anything -- gets out before allocating.
        if (!_replay.HasGhosts)
        {
            return;
        }

        List<(uint index, ulong owner)> ghosts = _replay.GhostEntities().ToList();

        foreach ((CCheckTransmitInfo info, CCSPlayerController? viewer) in infoList)
        {
            if (viewer == null || !viewer.IsValid)
            {
                continue;
            }

            bool viewerIsSolo = _system.IsSolo(viewer.SteamID);
            bool viewerWantsGhosts = _system.WantsGhosts(viewer.SteamID);

            foreach ((uint index, ulong owner) in ghosts)
            {
                // Somebody else's preview may already have been drawn before
                // this viewer turned theirs off, so the transmit filter answers
                // for their own as well.
                if (!viewerWantsGhosts)
                {
                    info.TransmitEntities.Remove(index);
                    continue;
                }

                if (owner == viewer.SteamID)
                {
                    continue;
                }

                if (viewerIsSolo || _system.IsSolo(owner))
                {
                    info.TransmitEntities.Remove(index);
                }
            }
        }
    }
}
