using FiveStack.Utilities;

namespace UtilityPractice;

// The compass between two draws of the bar: the pips, moved, and nothing
// else. The bar is built four times slower than the view can turn, and a pip
// that only hears where it is that often is seen to step.
public class NadeCompassTemplate : HudTemplate
{
    public override HudLayoutSlots Slots => HudSlots.Bar;

    public IReadOnlyList<(int Slot, int Column)> Moves { get; init; } =
        Array.Empty<(int, int)>();

    public override void Apply(HudSurface surface)
    {
        foreach ((int slot, int column) in Moves)
        {
            surface.Move($"p{slot + 1}", "x", Math.Clamp(column, 0, HudSlots.CompassColumns - 1));
        }
    }
}

// Everything on screen while the player is playing rather than choosing: the
// compass, what the keys do, and what the last key did.
public class NadeBarTemplate : HudTemplate
{
    // Slot is which of the fixed pips draws this spot. It stays with the spot
    // from one frame to the next, so a pip slides along the ribbon instead of
    // jumping to wherever another spot happened to be.
    // Fresh is the first frame a pip draws a spot it was not drawing before.
    // It is placed but kept hidden for that frame, so the glide from wherever
    // the pip last was happens out of sight.
    public record Pip(int Slot, int Column, string Type, bool Focus, bool Fresh = false);

    public record Key(string Label, bool On = false, bool Dim = false);

    // One smoke or fire in the world. Flight is how long it was, or has been,
    // in the air; Time what it has left. Type is one of RadarMaps.Types, Color
    // a throw colour's name or null, Fill how much of its life is left in
    // HudSlots.OutFillSteps steps.
    // Landed is the panel's verdict on a throw made at a lineup: null until
    // there is one, or when the throw was not scored.
    public record Out(
        string Name,
        string Flight,
        string Score,
        string Time,
        string Type,
        string? Color,
        int Fill,
        bool Air,
        bool Fading,
        bool? Landed
    );

    // A list down the left edge: the plan being worked through, or the stack
    // of throws that go again together. Both are a title, rows and a foot.
    public record CardRow(string Name, string Value, bool Now = false, bool Done = false);

    public record Card(string Title, string Sub, string Foot, IReadOnlyList<CardRow> Rows);

    // Total and Full are lengths of bar, in HudSlots.FlashBarSteps steps.
    public record FlashRow(string Who, string Seconds, string Tag, int Total, int Full, bool Bad);

    // Fire is the same card with no bars: whether a fire reached somebody,
    // not how long they stood in it.
    public record FlashCard(
        string Title,
        string Sub,
        string Foot,
        IReadOnlyList<FlashRow> Rows,
        bool Fire = false
    );

    public override HudLayoutSlots Slots => HudSlots.Bar;

    public bool Compass { get; init; }
    public IReadOnlyList<Pip> Pips { get; init; } = Array.Empty<Pip>();
    public string Left { get; init; } = "";
    public string Right { get; init; } = "";
    public string Focus { get; init; } = "";

    public bool Legend { get; init; }

    // One per legend row, in the layout's order. Null hides the row: a key
    // that does nothing right now is not on the list.
    public IReadOnlyList<Key?> Keys { get; init; } = Array.Empty<Key?>();

    public string? Toast { get; init; }

    // The first HudSlots.OutRows of what is out, and how many there are in all.
    public IReadOnlyList<Out> Outs { get; init; } = Array.Empty<Out>();
    public int OutTotal { get; init; }

    public Card? List { get; init; }
    public FlashCard? Flash { get; init; }

    public override void Apply(HudSurface surface)
    {
        surface.Class("compass", "hidden", !Compass);

        var drawn = new Pip?[HudSlots.CompassPips];

        foreach (Pip pip in Pips)
        {
            if (pip.Slot >= 0 && pip.Slot < drawn.Length)
            {
                drawn[pip.Slot] = pip;
            }
        }

        for (int slot = 0; slot < drawn.Length; slot++)
        {
            string id = $"p{slot + 1}";
            Pip? pip = drawn[slot];

            surface.Class(id, "hidden", pip == null || pip.Fresh);
            surface.Class(id, "focus", pip?.Focus == true);

            if (pip == null)
            {
                continue;
            }

            surface.Move(id, "x", Math.Clamp(pip.Column, 0, HudSlots.CompassColumns - 1));

            foreach (string type in RadarMaps.Types)
            {
                surface.Class(id, type, type == pip.Type);
            }
        }

        surface.Set("cleft", Left);
        surface.Set("cfocus", Focus);
        surface.Set("cright", Right);

        surface.Class("legend", "hidden", !Legend);

        for (int index = 0; index < HudSlots.LegendKeys; index++)
        {
            Key? key = index < Keys.Count ? Keys[index] : null;

            surface.Set($"k{index + 1}", key?.Label ?? "");
            surface.Set($"kb{index + 1}", HudSlots.LegendBindings[index]);
            surface.Class($"kr{index + 1}", "hidden", key == null);
            surface.Class($"kr{index + 1}", "on", key?.On == true);
            surface.Class($"kr{index + 1}", "dim", key?.Dim == true);
        }

        surface.Set("toast", Toast ?? "");
        surface.Class("toast", HudSlots.Shown, Toast != null);

        ApplyOuts(surface);
        ApplyList(surface);
        ApplyFlash(surface);
    }

    private void ApplyOuts(HudSurface surface)
    {
        surface.Class("out", HudSlots.Shown, Outs.Count > 0);

        if (Outs.Count == 0)
        {
            return;
        }

        surface.Set("ocount", OutTotal.ToString());
        surface.Set("omore", OutTotal > Outs.Count ? $"+ {OutTotal - Outs.Count} more" : "");

        for (int index = 0; index < HudSlots.OutRows; index++)
        {
            int row = index + 1;
            Out? entry = index < Outs.Count ? Outs[index] : null;

            surface.Class($"or{row}", HudSlots.Shown, entry != null);

            if (entry == null)
            {
                continue;
            }

            surface.Set($"on{row}", entry.Name);
            surface.Set($"of{row}", entry.Flight);
            surface.Set($"os{row}", entry.Score);
            surface.Set($"ot{row}", entry.Time);
            surface.Class($"or{row}", "air", entry.Air);
            surface.Class($"or{row}", "fade", entry.Fading);
            surface.Class($"or{row}", "landed", entry.Landed == true);
            surface.Class($"or{row}", "missed", entry.Landed == false);

            foreach (string type in HudSlots.OutTypes)
            {
                surface.Class($"or{row}", type, type == entry.Type);
            }

            for (int color = 0; color < PracticeStepColors.Count; color++)
            {
                string name = PracticeStepColors.For(color).Name;

                surface.Class($"or{row}", name, name == entry.Color);
            }

            surface.Pick($"ob{row}", "f", entry.Fill, HudSlots.OutFillSteps);
        }
    }

    private void ApplyList(HudSurface surface)
    {
        surface.Class("plan", HudSlots.Shown, List != null);

        if (List == null)
        {
            return;
        }

        surface.Set("pltitle", List.Title);
        surface.Set("plsub", List.Sub);
        surface.Set("plfoot", List.Foot);

        for (int index = 0; index < HudSlots.PlanRows; index++)
        {
            int row = index + 1;
            CardRow? entry = index < List.Rows.Count ? List.Rows[index] : null;

            surface.Class($"plr{row}", HudSlots.Shown, entry != null);

            if (entry == null)
            {
                continue;
            }

            surface.Set($"pl{row}", entry.Name);
            surface.Set($"plv{row}", entry.Value);
            surface.Class($"plr{row}", "now", entry.Now);
            surface.Class($"plr{row}", "done", entry.Done);
        }
    }

    private void ApplyFlash(HudSurface surface)
    {
        surface.Class("flash", HudSlots.Shown, Flash != null);

        if (Flash == null)
        {
            return;
        }

        surface.Class("flash", "fire", Flash.Fire);
        surface.Set("fltitle", Flash.Title);
        surface.Set("flsub", Flash.Sub);
        surface.Set("flfoot", Flash.Foot);

        for (int index = 0; index < HudSlots.FlashRows; index++)
        {
            int row = index + 1;
            FlashRow? entry = index < Flash.Rows.Count ? Flash.Rows[index] : null;

            surface.Class($"fr{row}", HudSlots.Shown, entry != null);

            if (entry == null)
            {
                continue;
            }

            surface.Set($"fw{row}", entry.Who);
            surface.Set($"fs{row}", entry.Seconds);
            surface.Set($"fg{row}", entry.Tag);
            surface.Class($"fr{row}", "bad", entry.Bad);
            surface.Pick($"fa{row}", "d", entry.Total, HudSlots.FlashBarSteps);
            surface.Pick($"fu{row}", "d", entry.Full, HudSlots.FlashBarSteps);
        }
    }
}
