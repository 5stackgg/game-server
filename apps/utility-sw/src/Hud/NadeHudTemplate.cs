using FiveStack.Entities.Practice;
using FiveStack.Utilities;

namespace UtilityPractice;

// The throw bar: what this throw is, the hands that make it, and the two
// things that have to be true before it leaves them.
public class NadeHudTemplate : HudTemplate
{
    public override HudLayoutSlots Slots => HudSlots.Hud;

    public required string Type { get; init; }

    // One of RadarMaps.Types, for the swatch beside the type.
    public required string TypeClass { get; init; }

    // "3 / 14", or empty when nothing is being walked through.
    public string Position { get; init; } = "";
    public required string Title { get; init; }
    public required string Technique { get; init; }

    // Which mouse buttons and keys the throw is made with. The bar draws
    // them; the words beside them only name the technique.
    public required ThrowKeys Keys { get; init; }

    public required bool OnSpot { get; init; }
    public required string Spot { get; init; }
    public required bool OnAngle { get; init; }
    public required string Aim { get; init; }

    // The colour the next smoke will come out in, as a PracticeStepColors
    // name. Null with colours off, and for everything that is not a smoke:
    // only a smoke is tinted, so only a smoke has a colour to promise.
    public string? ThrowColor { get; init; }

    // The meter under the locks: what it counts, the count, how full, and
    // whether the count has reached what it was counting to.
    public record Meter(string Key, string Label, int Tenths, bool Done = false);

    public Meter? Progress { get; init; }

    // Landed or not, oldest first: the last few throws at this lineup.
    public IReadOnlyList<bool> Last { get; init; } = Array.Empty<bool>();

    public static NadeHudTemplate For(
        LineupRecord lineup,
        float eyeYaw,
        float eyePitch,
        string? throwColor,
        bool onSpot,
        float away,
        bool onAngle,
        Meter? progress,
        string position,
        (float yaw, float pitch) target,
        IReadOnlyList<bool>? last = null
    )
    {
        ThrowKeys keys = PracticeThrowKeys.For(lineup);

        float error = PracticeLineupUtility.AimError(eyeYaw, eyePitch, target.yaw, target.pitch);
        string arrow = HudAimGrid.Arrow(eyeYaw, eyePitch, target.yaw, target.pitch);

        return new NadeHudTemplate
        {
            Type = lineup.utility_type == "HighExplosive" ? "HE" : lineup.utility_type,
            TypeClass = RadarMaps.TypeClass(lineup.utility_type),
            Title = DrillUtility.Name(lineup),
            Technique = keys.Word,
            Keys = keys,
            OnSpot = onSpot,
            Spot = onSpot ? "On spot" : PracticeLineupUtility.Metres(away),
            OnAngle = onAngle,
            Aim = onAngle ? "Lined up" : $"{Degrees(error)} {arrow}".TrimEnd(),
            ThrowColor = throwColor,
            Progress = progress,
            Position = position,
            Last = last ?? Array.Empty<bool>(),
        };
    }

    // Two decimals only where they mean something: inside a degree the
    // hundredths are the instruction, past ten they are noise.
    private static string Degrees(float error)
    {
        return error switch
        {
            < 1f => $"{error:0.00}°",
            < 10f => $"{error:0.0}°",
            _ => $"{error:0}°",
        };
    }

    private bool Held(string cap)
    {
        return cap switch
        {
            "shift" => Keys.Walk,
            "ctrl" => Keys.Crouch,
            "jump" => Keys.Jump,
            _ => Keys.Move.Contains(cap.ToUpperInvariant()),
        };
    }

    public override void Apply(HudSurface surface)
    {
        surface.Set("kicker", Type);
        surface.Set("title", Title);
        surface.Set("pos", Position);

        // The grenade it is and the body that throws it, as pictures. The type
        // class picks the drawing; the colour classes below tint it.
        foreach (string type in RadarMaps.Types)
        {
            surface.Class("nade", type, type == TypeClass);
        }

        foreach (string figure in PracticeThrowKeys.Figures)
        {
            surface.Class("fig", figure, figure == Keys.Figure);
        }

        // With colours on, the icon and the bar's edge wear the colour this
        // throw will come out in. Naming the colour in words beside an icon of
        // another colour said two different things.
        for (int index = 0; index < PracticeStepColors.Count; index++)
        {
            string name = PracticeStepColors.For(index).Name;
            bool worn = string.Equals(name, ThrowColor, StringComparison.OrdinalIgnoreCase);

            surface.Class("nade", name, worn);
            surface.Class(Slots.RootId, name, worn);
        }

        surface.Set("tech", Technique);
        surface.Set("runup", Keys.RunUp);
        surface.Class("runup", HudSlots.Shown, Keys.RunUp.Length > 0);

        surface.Class("mb1", "on", Keys.LeftClick);
        surface.Class("mb2", "on", Keys.RightClick);

        foreach (string cap in HudSlots.ThrowKeycaps)
        {
            surface.Class($"kc_{cap}", "on", Held(cap));
            surface.Set($"kb_{cap}", HudSlots.ThrowKeyBinding(cap));
        }

        surface.Set("spotval", Spot);
        surface.Class("spot", "on", OnSpot);
        surface.Set("aimerr", Aim);
        surface.Class("aim", "on", OnAngle);

        // Both locks at once is the whole instruction: the bar itself goes
        // green, which is readable from the corner of an eye.
        surface.Class(Slots.RootId, "go", OnSpot && OnAngle);

        surface.Class("drillrow", HudSlots.Shown, Progress != null);

        if (Progress != null)
        {
            surface.Set("drillkey", Progress.Key);
            surface.Set("drill", Progress.Label);
            surface.Class("drillrow", "done", Progress.Done);
            surface.Pick("drillbar", "p", Progress.Tenths, HudSlots.MeterSteps);
        }

        // Newest on the right; the dots left of the first throw stay empty.
        int empty = HudSlots.LastThrows - Math.Min(Last.Count, HudSlots.LastThrows);
        int first = Math.Max(0, Last.Count - HudSlots.LastThrows);

        for (int dot = 0; dot < HudSlots.LastThrows; dot++)
        {
            bool? landed = dot < empty ? null : Last[first + dot - empty];

            surface.Class($"ld{dot + 1}", "hit", landed == true);
            surface.Class($"ld{dot + 1}", "miss", landed == false);
        }
    }
}
