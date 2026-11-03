using System.Collections.Concurrent;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;
using SwiftlyS2.Shared.SchemaDefinitions;
using static SwiftlyS2.Shared.Helper;

namespace UtilityPractice;

public partial class UtilityPracticePlugin
{
    private readonly PracticeAfk _afk = new();

    // Player slots with any input since the last sweep. Taps are shorter than
    // the one-second sample, so the input paths mark them as they happen.
    private readonly ConcurrentDictionary<int, byte> _afkStirred = new();

    private Guid? _afkChatHook;
    private EventDelegates.OnClientVoice? _voiceHandler;

    private static int AfkNow => (int)(Environment.TickCount64 / 1000);

    private void Stir(int playerId)
    {
        _afkStirred[playerId] = 0;
    }

    private void WireAfk()
    {
        _afkChatHook = Core.Command.HookClientChat(
            (playerId, _, _) =>
            {
                Stir(playerId);

                return HookResult.Continue;
            }
        );

        _voiceHandler = @event => Stir(@event.PlayerId);
        Core.Event.OnClientVoice += _voiceHandler;
    }

    private void UnwireAfk()
    {
        if (_afkChatHook is { } hook)
        {
            Core.Command.UnhookClientChat(hook);
            _afkChatHook = null;
        }

        if (_voiceHandler != null)
        {
            Core.Event.OnClientVoice -= _voiceHandler;
            _voiceHandler = null;
        }

        _afkStirred.Clear();
        _afk.Reset();
    }

    private void AfkWatch()
    {
        if (_config.RenderMode)
        {
            return;
        }

        var samples = new List<PracticeAfk.Sample>();
        var players = new Dictionary<ulong, IPlayer>();

        foreach (IPlayer player in Core.PlayerManager.GetAllPlayers())
        {
            if (player == null || !player.IsValid || player.IsFakeClient)
            {
                continue;
            }

            players[player.SteamID] = player;
            samples.Add(AfkSample(player));
        }

        _afkStirred.Clear();

        int minutes = _session.Current?.afk_kick_minutes ?? 0;

        PracticeAfk.Dispatch(
            _afk.Sweep(samples, AfkNow, minutes),
            due =>
            {
                if (!players.TryGetValue(due.SteamId, out IPlayer? player))
                {
                    return;
                }

                if (due.Step == PracticeAfk.Step.Kick)
                {
                    AfkKick(player, due.IdleSeconds);

                    return;
                }

                AfkWarn(player, PracticeAfk.Warning(due.Step, due.SecondsLeft));
            },
            (due, error) =>
                _logger.LogWarning(error, "unable to act on {step} for {steam}", due.Step, due.SteamId)
        );
    }

    // A dead or spectating player's own pawn is frozen, held buttons and all,
    // so they are judged by the observer pawn they look through and by what
    // they press, say or type. A player who cannot be read is taken as present
    // rather than kicked on a fault.
    private PracticeAfk.Sample AfkSample(IPlayer player)
    {
        try
        {
            bool inGame = player.Controller.Connected == PlayerConnectedState.Connected;
            bool alive = player.IsAlive;
            bool stirred = _afkStirred.ContainsKey(player.PlayerID);
            ulong buttons = alive ? (ulong)player.PressedButtons : 0;

            if (!alive)
            {
                CBasePlayerPawn? observer = player.Pawn;

                if (observer == null || !observer.IsValid)
                {
                    return new PracticeAfk.Sample(player.SteamID, inGame, Alive: alive, stirred, 0, null, null);
                }

                bool roaming =
                    observer.ObserverServices?.ObserverMode == (byte)ObserverMode_t.OBS_MODE_ROAMING;
                QAngle view = observer.V_angle;
                Vector seat = observer.AbsOrigin ?? new Vector(0, 0, 0);

                return new PracticeAfk.Sample(
                    player.SteamID,
                    inGame,
                    Alive: alive,
                    stirred,
                    0,
                    (view.Y, view.X),
                    (seat.X, seat.Y, seat.Z),
                    Roaming: roaming
                );
            }

            CCSPlayerPawn? pawn = player.PlayerPawn;

            if (pawn == null || !pawn.IsValid)
            {
                return new PracticeAfk.Sample(player.SteamID, inGame, Alive: alive, stirred, buttons, null, null);
            }

            QAngle eyes = pawn.EyeAngles;
            Vector at = pawn.AbsOrigin ?? new Vector(0, 0, 0);

            return new PracticeAfk.Sample(
                player.SteamID,
                inGame,
                Alive: alive,
                stirred,
                buttons,
                (eyes.Y, eyes.X),
                (at.X, at.Y, at.Z)
            );
        }
        catch
        {
            return new PracticeAfk.Sample(player.SteamID, false, false, true, 0, null, null);
        }
    }

    private void AfkWarn(IPlayer player, string text)
    {
        Tell(player.SteamID, $" {ChatColors.Red}{text}");

        if (UseHud(player.SteamID) && _hud!.Available(HudSlots.Bar))
        {
            Toast(player, text);
        }
    }

    private void AfkKick(IPlayer player, int idleSeconds)
    {
        ulong steamId = player.SteamID;

        _afk.Forget(steamId);
        _logger.LogInformation("kicking {steam}: idle for {seconds} seconds", steamId, idleSeconds);

        player.Kick(
            PracticeAfk.KickReason(idleSeconds),
            ENetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED_IDLE
        );
    }
}
