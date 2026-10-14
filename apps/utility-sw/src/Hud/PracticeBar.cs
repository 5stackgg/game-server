using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using static SwiftlyS2.Shared.Helper;

namespace UtilityPractice;

// What is on screen while the player plays: where the spots are, what the
// keys do here, and what the last one did.
public partial class UtilityPracticePlugin
{
    // Sixteen times a second, the pace of the aim feedback it rides on. The
    // pips glide between cells on the client for exactly this long, so the
    // ribbon moves continuously at whatever rate the game is drawing.
    private const int BarEveryTicks = 4;

    // How close to dead ahead a pip has to be for the line under the ribbon
    // to name it.
    private const float CompassFocusDegrees = 6f;

    // A spot under the player's feet has no direction worth drawing.
    private const float CompassNearUnits = 48f;

    private const int ToastTicks = 64 * 3;

    private readonly HashSet<ulong> _compassOff = new();
    private readonly HashSet<ulong> _legendOff = new();
    private readonly Dictionary<ulong, string?[]> _pipSlots = new();
    private readonly Dictionary<ulong, (string Text, int Until)> _toasts = new();

    private sealed class CompassEntry
    {
        public required LineupSpot Spot;
        public required int Column;
        public required float Away;
        public required float Bearing;
    }

    // Where chat used to answer a command. Falls back to chat for a player
    // with no panels, so a key press is never met with silence.
    private void Toast(IPlayer player, string text)
    {
        if (!UseHud(player.SteamID) || !_hud!.Available(HudSlots.Bar))
        {
            Tell(player.SteamID, $" {ChatColors.Grey}{text}");

            return;
        }

        _toasts[player.SteamID] = (text, _aimTick + ToastTicks);
    }

    // A lineup name is whatever somebody typed. The legend has the left edge
    // of the screen and no business reaching the crosshair.
    private const int LegendNameLength = 26;

    private static string Clip(string text, int length)
    {
        return text.Length <= length ? text : text[..(length - 1)].TrimEnd() + "…";
    }

    private void ForgetBar(ulong steamId)
    {
        _compassOff.Remove(steamId);
        _legendOff.Remove(steamId);
        _pipSlots.Remove(steamId);
        _pipSpots.Remove(steamId);
        _toasts.Remove(steamId);
        _zoomed.Remove(steamId);
        _reloadDown.Remove(steamId);
        _tabDown.Remove(steamId);
    }

    // Said once a minute at most: this runs eight times a second for every
    // player, and a fault in it is the same fault every time.
    private const int BarFaultEveryTicks = 64 * 60;
    private int _barFaultAt = -BarFaultEveryTicks;

    private void HudBar(IPlayer player, CCSPlayerPawn pawn)
    {
        if (_aimTick % BarEveryTicks != 0)
        {
            return;
        }

        try
        {
            DrawBar(player, pawn);
        }
        catch (Exception error)
        {
            if (_aimTick - _barFaultAt >= BarFaultEveryTicks)
            {
                _barFaultAt = _aimTick;
                _logger.LogWarning(error, "unable to draw the hud bar for {steam}", player.SteamID);
            }
        }
    }

    private void DrawBar(IPlayer player, CCSPlayerPawn pawn)
    {
        ulong steamId = player.SteamID;

        if (!UseHud(steamId) || !_hud!.Available(HudSlots.Bar))
        {
            return;
        }

        Vector origin = pawn.AbsOrigin ?? new Vector(0, 0, 0);
        var at = new Vec3(origin.X, origin.Y, origin.Z);

        bool cleared = _replay.IsHidden(steamId);
        int total = _library.For(steamId).Count;

        (List<NadeBarTemplate.Pip> pips, string left, string right, string focus) = Compass(
            steamId,
            pawn.EyeAngles.Y,
            at
        );

        string? toast = null;

        if (_toasts.TryGetValue(steamId, out (string Text, int Until) said))
        {
            if (_aimTick < said.Until)
            {
                toast = said.Text;
            }
            else
            {
                _toasts.Remove(steamId);
            }
        }

        (IReadOnlyList<NadeBarTemplate.Out> outs, int outTotal) = OutsFor(steamId);

        _hud.Show(
            player,
            new NadeBarTemplate
            {
                Outs = outs,
                OutTotal = outTotal,
                List = PlanCard(steamId) ?? StackCard(steamId) ?? SummaryCard(steamId),
                Flash = CardFor(steamId),
                Compass = !_compassOff.Contains(steamId) && !cleared && total > 0,
                Pips = pips,
                Left = left,
                Right = right,
                Focus = focus,
                // The execute's own panel has the left edge while one runs,
                // and so does the dock while it is open.
                Legend =
                    !_legendOff.Contains(steamId) && !_playbook.Running && !CursorOpen(player),
                Keys = LegendFor(player, pawn, at, cleared),
                Toast = toast,
            }
        );
    }

    // Where the spot each pip is drawing stands, by slot, as the bar last drew
    // them. Null for a pip that is not on the ribbon.
    private readonly Dictionary<ulong, Vec3?[]> _pipSpots = new();

    // Every game tick, where the bar itself is drawn every fourth. Which spots
    // are on the ribbon changes at walking pace and is left to the bar; where
    // they sit along it changes as fast as a mouse moves, and is done here.
    private void CompassTick()
    {
        if (_hud == null || _pipSpots.Count == 0)
        {
            return;
        }

        foreach ((ulong steamId, Vec3?[] spots) in _pipSpots)
        {
            IPlayer? player = _system.Find(steamId);
            CCSPlayerPawn? pawn = player?.PlayerPawn;

            if (
                player == null
                || !player.IsValid
                || pawn == null
                || !pawn.IsValid
                || pawn.AbsOrigin is not Vector origin
                || _compassOff.Contains(steamId)
                || _replay.IsHidden(steamId)
                || !_hud.IsOpen(player, HudSlots.Bar)
            )
            {
                continue;
            }

            var at = new Vec3(origin.X, origin.Y, origin.Z);
            float yaw = pawn.EyeAngles.Y;
            List<(int, int)>? moves = null;

            for (int slot = 0; slot < spots.Length; slot++)
            {
                if (spots[slot] is not Vec3 spot)
                {
                    continue;
                }

                int column = PracticeMarkerUtility.CompassColumn(
                    PracticeMarkerUtility.Bearing(yaw, at, spot),
                    HudSlots.CompassHalfSpanDegrees,
                    HudSlots.CompassColumns
                );

                // Off the end of the ribbon is the bar's to hide.
                if (column >= 0)
                {
                    (moves ??= new List<(int, int)>()).Add((slot, column));
                }
            }

            if (moves != null)
            {
                _hud.Show(player, new NadeCompassTemplate { Moves = moves });
            }
        }
    }

    private (List<NadeBarTemplate.Pip>, string, string, string) Compass(
        ulong steamId,
        float eyeYaw,
        Vec3 at
    )
    {
        var ahead = new List<CompassEntry>();
        int left = 0;
        int right = 0;

        foreach (LineupSpot spot in _replay.SpotsFor(steamId))
        {
            float away = Flat(spot.At, at);

            if (away < CompassNearUnits)
            {
                continue;
            }

            float bearing = PracticeMarkerUtility.Bearing(eyeYaw, at, spot.At);
            int column = PracticeMarkerUtility.CompassColumn(
                bearing,
                HudSlots.CompassHalfSpanDegrees,
                HudSlots.CompassColumns
            );

            if (column < 0)
            {
                if (bearing > 0f)
                {
                    left++;
                }
                else
                {
                    right++;
                }

                continue;
            }

            ahead.Add(
                new CompassEntry
                {
                    Spot = spot,
                    Column = column,
                    Away = away,
                    Bearing = bearing,
                }
            );
        }

        List<CompassEntry> nearest = ahead
            .OrderBy(entry => entry.Away)
            .Take(HudSlots.CompassPips)
            .ToList();

        CompassEntry? focus = nearest
            .Where(entry => Math.Abs(entry.Bearing) <= CompassFocusDegrees)
            .OrderBy(entry => Math.Abs(entry.Bearing))
            .FirstOrDefault();

        if (!_pipSlots.TryGetValue(steamId, out string?[]? slots))
        {
            slots = new string?[HudSlots.CompassPips];
            _pipSlots[steamId] = slots;
        }

        var drawn = nearest.Select(entry => entry.Spot.Key).ToHashSet(StringComparer.Ordinal);

        for (int slot = 0; slot < slots.Length; slot++)
        {
            if (slots[slot] != null && !drawn.Contains(slots[slot]!))
            {
                slots[slot] = null;
            }
        }

        var pips = new List<NadeBarTemplate.Pip>(nearest.Count);

        if (!_pipSpots.TryGetValue(steamId, out Vec3?[]? spots))
        {
            spots = new Vec3?[HudSlots.CompassPips];
            _pipSpots[steamId] = spots;
        }

        Array.Clear(spots);

        foreach (CompassEntry entry in nearest)
        {
            int slot = Array.IndexOf(slots, entry.Spot.Key);
            bool fresh = slot < 0;

            if (fresh)
            {
                slot = Array.IndexOf(slots, null);

                if (slot < 0)
                {
                    continue;
                }

                slots[slot] = entry.Spot.Key;
            }
            else
            {
                // A pip drawing a spot for the first time is placed out of
                // sight this frame, and must not be moved until it is shown.
                spots[slot] = entry.Spot.At;
            }

            pips.Add(
                new NadeBarTemplate.Pip(
                    slot,
                    entry.Column,
                    RadarMaps.TypeClass(entry.Spot.Lineups[0].utility_type),
                    entry == focus,
                    fresh
                )
            );
        }

        return (
            pips,
            left > 0 ? $"‹ {left}" : "",
            right > 0 ? $"{right} ›" : "",
            focus == null
                ? ""
                : $"{SpotName(focus.Spot)} · {PracticeLineupUtility.Metres(focus.Away)}"
        );
    }

    private string SpotName(LineupSpot spot)
    {
        if (spot.Lineups.Count == 1)
        {
            return PracticeLineupUtility.TitleCase(DrillUtility.Name(spot.Lineups[0]));
        }

        string place = _callouts.Label(spot.At);

        return $"{(place.Length > 0 ? place : "Spot")} · {spot.Lineups.Count} throws";
    }

    // One entry per row of the legend, in the layout's order: TAB, E, R. Every
    // row is always there: the legend is how a player learns the keys exist,
    // and a key that only shows up when it applies is one nobody goes looking
    // for. What changes is the words, to say what it would do from here.
    private IReadOnlyList<NadeBarTemplate.Key?> LegendFor(
        IPlayer player,
        CCSPlayerPawn pawn,
        Vec3 at,
        bool cleared
    )
    {
        ulong steamId = player.SteamID;
        LineupRecord? again = RethrowTarget(steamId);
        bool rethrows = again != null && again.IsExactlyReplayable() && _config.ReplayEnabled;
        bool zoomed = _zoomed.ContainsKey(steamId);
        bool following = IsFollowing(steamId);
        bool noclip = _system.StateFor(steamId).Noclip;

        // Reload is whichever of its three jobs it would do if pressed now.
        int stacked = StackCount(steamId);

        string reload =
            following ? "Following · press to come back"
            : HasFlight(steamId) ? "Follow the grenade"
            : zoomed ? "Zoomed"
            : stacked > 1 && _config.ReplayEnabled ? $"Rethrow all {stacked} · hold to zoom"
            : rethrows ? "Rethrow · hold to zoom"
            : "Hold to zoom";

        return new NadeBarTemplate.Key?[]
        {
            new NadeBarTemplate.Key("Menu"),
            UseKey(steamId, at, cleared),
            new NadeBarTemplate.Key(reload, zoomed || following),
            new NadeBarTemplate.Key("Add a bot here"),
            new NadeBarTemplate.Key(noclip ? "Noclip · on" : "Noclip", noclip),
            AimRingKey(steamId),
            new NadeBarTemplate.Key("Clear smoke and fire · twice for bots"),
        };
    }

    // Also where a drill's fading ring is explained: it says so while it is
    // happening, which is the only time anybody wonders where it went.
    private NadeBarTemplate.Key AimRingKey(ulong steamId)
    {
        if (!_system.StateFor(steamId).Crosshair)
        {
            return new NadeBarTemplate.Key("Aim ring hidden · show it", On: true);
        }

        float assist = _drill.Assist(steamId);

        if (assist <= 0f)
        {
            return new NadeBarTemplate.Key("Aim ring off · throw it from memory", On: true);
        }

        if (assist < 1f)
        {
            return new NadeBarTemplate.Key("Aim ring fading · it goes as you land it", On: true);
        }

        return new NadeBarTemplate.Key(
            _drill.Current(steamId) != null ? "Aim ring · fades as you land it" : "Hide the aim ring"
        );
    }

    // What use would do if it were pressed now, in the order UsePressed
    // decides it. With nothing for it to do, the row stays and dims: the key
    // is still worth knowing about.
    private NadeBarTemplate.Key UseKey(ulong steamId, Vec3 at, bool cleared)
    {
        if (cleared)
        {
            return new NadeBarTemplate.Key("Snap onto a spot nearby", Dim: true);
        }

        IReadOnlyList<LineupRecord> library = Shown(steamId);
        List<LineupRecord> here = PracticeReplay.SpotAt(library, at);

        if (CanCycle(steamId, here, at))
        {
            int index = here.IndexOf(_system.StateFor(steamId).Loaded!);

            return new NadeBarTemplate.Key($"Next throw from here · {index + 1} of {here.Count}");
        }

        LineupRecord? nearest = Nearest(library, at);
        float away = nearest == null ? float.MaxValue : Flat(nearest.release.feet_position, at);

        // In reach and not already on it: use finishes the walk.
        if (away <= UseReachUnits && away > PracticeLineupUtility.StanceToleranceUnits)
        {
            return new NadeBarTemplate.Key("Snap onto the spot");
        }

        return new NadeBarTemplate.Key("Snap onto a spot nearby", Dim: true);
    }
}
