using FiveStack.Utilities;

namespace UtilityPractice;

// One wheel of the radial menu. The layout has eight wedges and a hub and no
// idea what they are for; whichever wheel is open is written into them.
public class NadeWheelTemplate : HudTemplate
{
    public record Wedge(
        string Label,
        string Sub = "",
        bool Opens = false,
        bool On = false,
        bool Picked = false,
        bool Disabled = false
    );

    public override HudLayoutSlots Slots => HudSlots.Wheel;

    public override bool CapturesInput => true;

    public required string Title { get; init; }
    public required string Hint { get; init; }

    // By slot, clockwise from the top. Null leaves the slot empty.
    public required IReadOnlyList<Wedge?> Wedges { get; init; }

    // The outer ring, clockwise: 2k and 2k + 1 sit over wedge k. Only the
    // label and the on and disabled states are drawn.
    public IReadOnlyList<Wedge?> Quick { get; init; } = Array.Empty<Wedge?>();

    // The outer ring again, one per wedge and as wide as it: for a wedge with
    // a single quick action, in place of its two halves.
    public IReadOnlyList<Wedge?> Wide { get; init; } = Array.Empty<Wedge?>();

    public override void Apply(HudSurface surface)
    {
        surface.Set("hubt", Title);
        surface.Set("hubs", Hint);

        for (int index = 0; index < HudSlots.WheelSlots; index++)
        {
            int slot = index + 1;
            Wedge? wedge = index < Wedges.Count ? Wedges[index] : null;

            surface.Set($"w{slot}", wedge?.Label ?? "");
            surface.Set($"s{slot}", wedge?.Sub ?? "");
            surface.Class($"r{slot}", "blank", wedge == null);
            surface.Class($"r{slot}", "cat", wedge?.Opens == true);
            surface.Class($"r{slot}", "on", wedge?.On == true);
            surface.Class($"r{slot}", "pick", wedge?.Picked == true);
            surface.Class($"r{slot}", "disabled", wedge?.Disabled == true);
        }

        for (int index = 0; index < HudSlots.WheelQuicks; index++)
        {
            int slot = index + 1;
            Wedge? quick = index < Quick.Count ? Quick[index] : null;

            surface.Set($"q{slot}", quick?.Label ?? "");
            surface.Class($"o{slot}", "blank", quick == null);
            surface.Class($"o{slot}", "on", quick?.On == true);
            surface.Class($"o{slot}", "disabled", quick?.Disabled == true);
        }

        for (int index = 0; index < HudSlots.WheelSlots; index++)
        {
            int slot = index + 1;
            Wedge? wide = index < Wide.Count ? Wide[index] : null;

            surface.Set($"xl{slot}", wide?.Label ?? "");
            surface.Class($"x{slot}", "blank", wide == null);
            surface.Class($"x{slot}", "on", wide?.On == true);
            surface.Class($"x{slot}", "disabled", wide?.Disabled == true);
        }
    }

    // Any of a wedge's hit rectangles is the wedge: "r3h2" answers "slot:2".
    // On the outer ring "o5h1" answers "quick:4" and "x3h2" answers "wide:2".
    public override string Resolve(string buttonId)
    {
        int split = buttonId.IndexOf('h');

        if (
            buttonId.StartsWith("x", StringComparison.Ordinal)
            && split > 1
            && int.TryParse(buttonId.AsSpan(1, split - 1), out int wide)
            && wide >= 1
            && wide <= HudSlots.WheelSlots
        )
        {
            return $"wide:{wide - 1}";
        }

        if (
            buttonId.StartsWith("o", StringComparison.Ordinal)
            && split > 1
            && int.TryParse(buttonId.AsSpan(1, split - 1), out int quick)
            && quick >= 1
            && quick <= HudSlots.WheelQuicks
        )
        {
            return $"quick:{quick - 1}";
        }

        if (
            buttonId.StartsWith("r", StringComparison.Ordinal)
            && split > 1
            && int.TryParse(buttonId.AsSpan(1, split - 1), out int slot)
            && slot >= 1
            && slot <= HudSlots.WheelSlots
        )
        {
            return $"slot:{slot - 1}";
        }

        return buttonId;
    }
}
