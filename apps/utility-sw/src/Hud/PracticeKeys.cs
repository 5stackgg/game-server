using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace UtilityPractice;

// The keys a practice server takes over. Each is one a player already has
// bound and has no use for here: the scoreboard, and reload on a server where
// nothing runs out. Use keeps the job it had, and gains one.
//
// Reload is two keys in one, split by how long it is held. A tap throws the
// last one again; held, it zooms until it is let go. Nothing has to be in a
// particular state first, which is what made the earlier pin-pulled rule
// something a player had to be told.
//
// With a grenade of the player's own in the air it is a third: it rides along
// behind it. That one needs no rule either -- there is nothing to throw again
// until the first has landed.
//
// Crouch and walk keep their jobs and gain one each on a double tap, which is
// a thing nobody does to either by accident: a bot where you stand, and
// noclip.
//
// Not the number keys: six to zero are how a player reaches a grenade, and
// the server never sees a number pressed, only the weapon it switched to. Not
// any key the game has nothing bound to, for the same reason: an unbound key
// never leaves the client. Delete is bound, to selling back what was bought,
// on a server where nothing is. A keyboard with no delete key can bind any
// key it does have to the wipe command.
public partial class UtilityPracticePlugin
{
    private EventDelegates.OnClientKeyStateChanged? _keyHandler;
    private Guid? _commandHook;

    // What the default bind for delete sends.
    private const string SellBackCommand = "sellbackall";

    // Two presses of one key inside a third of a second are one gesture.
    private const int DoubleTapTicks = 21;

    // When each key was last pressed once, waiting to see if it is twice.
    private readonly Dictionary<(ulong steamId, string key), int> _tapped = new();

    // IN_RELOAD, read the way UseWatch reads IN_USE.
    private const uint InReload = 1 << 13;

    // The first zoom step of a scoped rifle. Tighter than this and the hand
    // cannot hold the crosshair still: the view narrows but the mouse does not
    // slow down with it the way a real scope's does.
    private const uint ZoomFov = 40;

    // Who is zoomed, and the field of view to hand back.
    private readonly Dictionary<ulong, uint> _zoomed = new();

    // A fifth of a second. Shorter and a deliberate tap zooms; longer and the
    // zoom feels like it is thinking about it.
    private const int ZoomHoldTicks = 13;

    // The same line for the scoreboard key: under it is a tap, which is the
    // menu; over it is a look at the scoreboard, which is not.
    private const int TapTicks = 13;

    // When the scoreboard key went down, for whoever is still holding it.
    private readonly Dictionary<ulong, int> _tabDown = new();

    // When reload went down, for whoever is still holding it.
    private readonly Dictionary<ulong, int> _reloadDown = new();

    private void WireKeys()
    {
        _keyHandler = OnKey;
        Core.Event.OnClientKeyStateChanged += _keyHandler;
        _commandHook = Core.Command.HookClientCommand(OnClientCommand);
    }

    private HookResult OnClientCommand(int playerId, string commandLine)
    {
        if (_tornDown || _config.RenderMode)
        {
            return HookResult.Continue;
        }

        string command = commandLine.Trim().Split(' ')[0];

        if (command != SellBackCommand)
        {
            return HookResult.Continue;
        }

        Core.Scheduler.NextTick(() =>
        {
            IPlayer? player = Core.PlayerManager.GetPlayer(playerId);

            if (player == null || !player.IsValid || player.IsFakeClient)
            {
                return;
            }

            try
            {
                ClearPressed(player);
            }
            catch (Exception error)
            {
                _logger.LogWarning(error, "{command} failed for {steam}", command, player.SteamID);
            }
        });

        return HookResult.Stop;
    }

    // True on the second press of two. The first is remembered and otherwise
    // left alone, so the key still does whatever it always did.
    private bool DoubleTapped(ulong steamId, string key)
    {
        if (_tapped.Remove((steamId, key), out int before) && _aimTick - before <= DoubleTapTicks)
        {
            return true;
        }

        _tapped[(steamId, key)] = _aimTick;

        return false;
    }

    // Once clears what was thrown; twice clears the bots as well. The first
    // press has already done its half by the time the second arrives, so
    // nothing waits to find out which it was.
    private void ClearPressed(IPlayer player)
    {
        if (DoubleTapped(player.SteamID, SellBackCommand))
        {
            Do(player, "nobots");

            return;
        }

        Do(player, "sweep");
    }

    private void UnwireKeys()
    {
        if (_keyHandler != null)
        {
            Core.Event.OnClientKeyStateChanged -= _keyHandler;
            _keyHandler = null;
        }

        if (_commandHook is { } hook)
        {
            Core.Command.UnhookClientCommand(hook);
            _commandHook = null;
        }

        // A reload that left somebody zoomed would leave them zoomed for good:
        // the next instance has no record that it was ever done.
        UnzoomEveryone();
        EndFollowEveryone();
        _tapped.Clear();
    }

    private void OnKey(IOnClientKeyStateChangedEvent @event)
    {
        KeyKind key = @event.Key;

        if (key is not (KeyKind.Tab or KeyKind.R or KeyKind.Ctrl or KeyKind.Shift or KeyKind.F))
        {
            return;
        }

        int playerId = @event.PlayerId;
        bool pressed = @event.Pressed;

        // The event is raised from inside the engine reading a player's input,
        // and what a key does here spawns and despawns entities. That gets a
        // tick of its own.
        Core.Scheduler.NextTick(() => KeyChanged(playerId, key, pressed));
    }

    private void KeyChanged(int playerId, KeyKind key, bool pressed)
    {
        if (_tornDown || _config.RenderMode)
        {
            return;
        }

        IPlayer? player = Core.PlayerManager.GetPlayer(playerId);

        if (player == null || !player.IsValid || player.IsFakeClient)
        {
            return;
        }

        // Caught here because nothing above this is ours: an exception out of
        // a scheduled call lands in the runtime, not in a log.
        try
        {
            switch (key)
            {
                case KeyKind.Tab:
                    if (pressed)
                    {
                        // Closing is immediate: a panel holding the cursor
                        // with no obvious way to give it back is the worst
                        // thing a menu can do to somebody mid-throw.
                        if (CursorOpen(player))
                        {
                            CloseCursorPanels(player);
                            _tabDown.Remove(player.SteamID);
                        }
                        else
                        {
                            _tabDown[player.SteamID] = _aimTick;
                        }

                        break;
                    }

                    // Opening waits for the key to come back up. Held, it was
                    // somebody reading the scoreboard, and the menu stays
                    // out of their way; tapped, the scoreboard has already
                    // gone by the time the wheel is drawn.
                    if (
                        _tabDown.Remove(player.SteamID, out int tabbed)
                        && _aimTick - tabbed < TapTicks
                    )
                    {
                        Unzoom(player);
                        EndFollow(player.SteamID);
                        OpenWheel(player);
                    }

                    break;
                case KeyKind.R:
                    ReloadChanged(player, pressed);

                    break;
                case KeyKind.Ctrl:
                    if (pressed && !CursorOpen(player) && DoubleTapped(player.SteamID, "ctrl"))
                    {
                        Do(player, "bot");
                    }

                    break;
                case KeyKind.Shift:
                    if (pressed && !CursorOpen(player) && DoubleTapped(player.SteamID, "shift"))
                    {
                        Do(player, "noclip");
                    }

                    break;
                // Inspect: bound by default, and twice in a row does nothing
                // else worth keeping.
                case KeyKind.F:
                    if (pressed && !CursorOpen(player) && DoubleTapped(player.SteamID, "inspect"))
                    {
                        Do(player, "crosshair");
                    }

                    break;
            }
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "key {key} failed for {steam}", key, player.SteamID);
        }
    }

    private void ReloadChanged(IPlayer player, bool pressed)
    {
        ulong steamId = player.SteamID;

        if (pressed)
        {
            if (CursorOpen(player))
            {
                return;
            }

            // Pressed again while riding along is how a tapped follow is left
            // before the grenade lands.
            if (_following.ContainsKey(steamId))
            {
                EndFollow(steamId);

                return;
            }

            if (StartFollow(player))
            {
                Toast(player, "Following · R to come back");

                return;
            }

            _reloadDown[steamId] = _aimTick;

            return;
        }

        // Held, the follow lasts as long as the key does. Tapped, it runs to
        // the landing by itself.
        if (_following.TryGetValue(steamId, out Follow? follow) && follow.KeyDown)
        {
            follow.KeyDown = false;

            if (_aimTick - follow.Pressed >= ZoomHoldTicks)
            {
                EndFollow(steamId);
            }

            return;
        }

        bool tapped =
            _reloadDown.Remove(steamId, out int since)
            && !_zoomed.ContainsKey(steamId)
            && _aimTick - since < ZoomHoldTicks;

        Unzoom(player);

        if (tapped)
        {
            Rethrow(player);
        }
    }

    // Any panel that has the cursor. While one is up the keys mean the panel,
    // not the game.
    private bool CursorOpen(IPlayer player)
    {
        return _hud != null
            && (
                _hud.IsOpen(player, HudSlots.Wheel)
                || _hud.IsOpen(player, HudSlots.Pick)
                || _hud.IsOpen(player, HudSlots.Edit)
            );
    }

    private void CloseCursorPanels(IPlayer player)
    {
        CloseWheel(player);
        ClosePicker(player);

        // Closing the editor cancels whatever it was asking for in chat, and
        // a save started from the wheel is asking the same way.
        if (_hud != null && _hud.IsOpen(player, HudSlots.Edit))
        {
            CloseEdit(player);
        }
    }

    // Whatever is in the hand. It is for reading an aim point, and a player
    // walks up to one with a rifle out as often as with the grenade.
    private void Zoom(IPlayer player)
    {
        ulong steamId = player.SteamID;

        if (_zoomed.ContainsKey(steamId) || CursorOpen(player))
        {
            return;
        }

        try
        {
            CCSPlayerController controller = player.Controller;

            _zoomed[steamId] = controller.DesiredFOV;
            controller.DesiredFOV = ZoomFov;
            controller.DesiredFOVUpdated();
        }
        catch (Exception error)
        {
            _zoomed.Remove(steamId);
            _logger.LogWarning(error, "unable to zoom {steam}", steamId);
        }
    }

    private void Unzoom(IPlayer player)
    {
        if (!_zoomed.Remove(player.SteamID, out uint was))
        {
            return;
        }

        try
        {
            CCSPlayerController controller = player.Controller;

            controller.DesiredFOV = was;
            controller.DesiredFOVUpdated();
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "unable to restore the view of {steam}", player.SteamID);
        }
    }

    private void UnzoomEveryone()
    {
        foreach (ulong steamId in _zoomed.Keys.ToList())
        {
            IPlayer? player = _system.Find(steamId);

            if (player != null && player.IsValid)
            {
                Unzoom(player);
            }
        }

        _zoomed.Clear();
    }

    // Runs for every player a few times a second: starts the zoom once reload
    // has been down long enough to be a hold, and ends it when the release
    // never arrived -- the key let go while the console was open -- which
    // would otherwise leave the view narrowed until somebody worked out which
    // key to tap.
    private void ZoomWatch(IPlayer player, CCSPlayerPawn pawn)
    {
        ulong steamId = player.SteamID;
        bool zoomed = _zoomed.ContainsKey(steamId);
        bool down = _reloadDown.TryGetValue(steamId, out int since);

        if (!zoomed && !down)
        {
            return;
        }

        uint buttons = 0;

        try
        {
            buttons = (uint)(pawn.MovementServices?.Buttons.ButtonStates[0] ?? 0);
        }
        catch
        {
            buttons = 0;
        }

        bool held = (buttons & InReload) != 0;

        if (zoomed)
        {
            if (!held)
            {
                _reloadDown.Remove(steamId);
                Unzoom(player);
            }

            return;
        }

        if (!held)
        {
            // Long enough that the release itself has been heard if it was
            // ever going to be.
            if (_aimTick - since > ZoomHoldTicks * 2)
            {
                _reloadDown.Remove(steamId);
            }

            return;
        }

        if (_aimTick - since >= ZoomHoldTicks)
        {
            Zoom(player);
        }
    }

    private LineupRecord? RethrowTarget(ulong steamId)
    {
        return _system.StateFor(steamId).Loaded ?? _recorder.LastThrow(steamId);
    }

    // The grenade again, from where it was thrown, with the player left where
    // they are: stand off to the side and watch the line you cannot see while
    // you are the one making it.
    private void Rethrow(IPlayer player)
    {
        if (StackCount(player.SteamID) > 1)
        {
            RethrowStack(player);

            return;
        }

        RethrowOne(player);
    }

    private void RethrowOne(IPlayer player)
    {
        if (!_config.ReplayEnabled)
        {
            Toast(player, "Replay is off on this server");

            return;
        }

        LineupRecord? lineup = RethrowTarget(player.SteamID);

        if (lineup == null)
        {
            Toast(player, "Throw something first");

            return;
        }

        // A lineup fitted to a demo has a path but no seed, so there is
        // nothing to hand the engine.
        if (!lineup.IsExactlyReplayable())
        {
            Toast(player, $"{DrillUtility.Name(lineup)} has no recorded throw to replay");

            return;
        }

        _replay.ThrowGhostProjectile(player, lineup, force: true);
        Toast(player, $"Rethrown · {DrillUtility.Name(lineup)}");
    }

    private static float Flat(Vec3 a, Vec3 b)
    {
        return new Vec3(a.x - b.x, a.y - b.y, 0f).LengthXY();
    }

    // Standing exactly on the loaded throw, at a spot with others to choose
    // from. Only then does use mean "the next one": anywhere else it still
    // means "put me on that".
    private bool CanCycle(ulong steamId, List<LineupRecord> here, Vec3 at)
    {
        LineupRecord? loaded = _system.StateFor(steamId).Loaded;

        return here.Count > 1
            && loaded != null
            && here.Contains(loaded)
            && _drill.Current(steamId) == null
            && Flat(loaded.release.feet_position, at) <= PracticeLineupUtility.StanceToleranceUnits;
    }

    private void UsePressed(IPlayer player, CCSPlayerPawn pawn)
    {
        try
        {
            Use(player, pawn);
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "use failed for {steam}", player.SteamID);
        }
    }

    private void Use(IPlayer player, CCSPlayerPawn pawn)
    {
        ulong steamId = player.SteamID;

        if (CursorOpen(player))
        {
            return;
        }

        // Cleared means cleared. Use is how a player picks things up and opens
        // doors as well, and markers coming back because of that is not what
        // they asked for; the menu turns them back on.
        if (_replay.IsHidden(steamId))
        {
            return;
        }

        if (!NextThrowHere(player, pawn))
        {
            StandOnNearest(player, pawn);
        }
    }

    // False when there is no next throw to step to from where they stand.
    private bool NextThrowHere(IPlayer player, CCSPlayerPawn pawn)
    {
        ulong steamId = player.SteamID;
        Vector origin = pawn.AbsOrigin ?? new Vector(0, 0, 0);
        var at = new Vec3(origin.X, origin.Y, origin.Z);
        List<LineupRecord> here = PracticeReplay.SpotAt(Shown(steamId), at);

        if (!CanCycle(steamId, here, at))
        {
            return false;
        }

        PracticeState state = _system.StateFor(steamId);
        int next = (here.IndexOf(state.Loaded!) + 1) % here.Count;
        LineupRecord lineup = here[next];

        if (!_replay.StandOn(player, lineup))
        {
            return false;
        }

        state.Loaded = lineup;
        _replay.GiveUtility(player, lineup.utility_type);

        // The watcher only redraws when the set under the player changes, and
        // this changed which of the same set is the one in hand.
        _standingIn.Remove(steamId);

        Toast(
            player,
            $"{PracticeLineupUtility.TitleCase(DrillUtility.Name(lineup))} · {next + 1} of {here.Count}"
        );

        return true;
    }

    private void ShowMarkers(IPlayer player)
    {
        _replay.Unhide(player.SteamID);
        _standingIn.Remove(player.SteamID);
        ShowWorldFor(player.SteamID);
    }

    private void HideMarkers(IPlayer player)
    {
        _replay.HideWorld(player.SteamID);
        _standingIn.Remove(player.SteamID);
    }
}
