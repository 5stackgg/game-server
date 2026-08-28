using FiveStack.Entities.Practice;
using FiveStack.Utilities;

namespace UtilityPractice;

// The passive guidance panel: what this lineup is, how it is thrown, and how
// far the crosshair still has to travel.
public class NadeHudTemplate : HudTemplate
{
    public override HudLayoutSlots Slots => HudSlots.Hud;

    public required string Kicker { get; init; }

    // "3 / 14", or empty when nothing is being walked through.
    public string Position { get; init; } = "";
    public required string Title { get; init; }
    public required string Technique { get; init; }

    public required bool OnSpot { get; init; }

    // Whether the player has committed to this lineup -- loaded it, or is being
    // drilled on it. Docks the panel; deliberately not the on-spot test, which
    // flickers at the circle's edge and made every .load flash the panel from
    // the top of the screen to the bottom.
    public required bool Settled { get; init; }
    public required bool OnAngle { get; init; }

    public required float AimErrorDegrees { get; init; }
    public required string Steer { get; init; }

    // Null when the player has colours off, or when nothing is about to be
    // thrown. The chip hides rather than showing a stale colour.
    public string? ThrowColor { get; init; }

    public bool HasWriteUp { get; init; }
    public string? Drill { get; init; }
    public int DrillTenths { get; init; }

    public static NadeHudTemplate For(
        LineupRecord lineup,
        float eyeYaw,
        float eyePitch,
        float tolerance,
        string? throwColor,
        bool onSpot,
        bool settled,
        bool onAngle,
        string? drill,
        int drillTenths,
        string position,
        string lands
    )
    {
        return new NadeHudTemplate
        {
            // Where it lands beats which side it is for: the side is implied by
            // the spot you are standing on, the destination is the whole point.
            Kicker = lands.Length > 0
                ? $"{lineup.utility_type} · {lands}"
                : $"{lineup.utility_type} · {(lineup.side == "CT" ? "CT" : "T")} side",
            Title = PracticeLineupUtility.TitleCase(lineup.name),
            Technique = PracticeLineupUtility.TitleCase(PracticeReplay.ThrowHint(lineup)),
            OnSpot = onSpot,
            Settled = settled,
            OnAngle = onAngle,
            AimErrorDegrees = PracticeLineupUtility.AimError(
                eyeYaw,
                eyePitch,
                lineup.release.yaw,
                lineup.release.pitch
            ),
            Steer = HudAimGrid.Direction(
                eyeYaw,
                eyePitch,
                lineup.release.yaw,
                lineup.release.pitch,
                tolerance
            ),
            ThrowColor = throwColor,
            HasWriteUp = !string.IsNullOrWhiteSpace(lineup.description),
            Drill = drill,
            DrillTenths = drillTenths,
            Position = position,
        };
    }

    public override void Apply(HudSurface surface)
    {
        surface.Set("kicker", Kicker);
        surface.Set("pos", Position);
        surface.Set("title", Title);
        surface.Set("tech", Technique);
        surface.Set("aimerr", $"{AimErrorDegrees:0.00}°");
        surface.Set("steer", Steer);

        surface.Class(Slots.RootId, "docked", Settled);

        surface.Class("spot", "on", OnSpot);
        surface.Class("aim", "on", OnAngle);
        surface.Class("aimerr", "on", OnAngle);
        surface.Class("steer", "on", OnAngle);

        surface.Set("throwcolor", ThrowColor ?? "");
        surface.Class("throwcolor", "hidden", ThrowColor == null);

        for (int index = 0; index < PracticeStepColors.Count; index++)
        {
            string name = PracticeStepColors.For(index).Name;

            surface.Class("throwcolor", name, name == ThrowColor);
        }

        surface.Class("noterow", HudSlots.Shown, HasWriteUp);

        bool drilling = Drill != null;

        surface.Class("drillrow", HudSlots.Shown, drilling);

        if (drilling)
        {
            surface.Set("drill", Drill!);
            surface.Pick("drillbar", "p", DrillTenths, HudSlots.MeterSteps);
        }
    }
}
