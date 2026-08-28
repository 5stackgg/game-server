using FiveStack.Utilities;

namespace UtilityPractice;

public class NadeEditTemplate : HudTemplate
{
    public override HudLayoutSlots Slots => HudSlots.Edit;

    public override bool CapturesInput => true;

    public required string Tag { get; init; }
    public required string Title { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Visibility { get; init; }
    public required string Hint { get; init; }

    // "", "warn", "bad" or "good". The hint is the only thing that reports what
    // happened to a save, so it has to be able to look wrong.
    public string HintTone { get; init; } = "";

    // Which field is waiting for a chat answer, if any.
    public string? Asking { get; init; }

    public override void Apply(HudSurface surface)
    {
        surface.Set("tag", Tag);
        surface.Set("title", Title);
        surface.Set("fname", Name);
        surface.Set("fdesc", string.IsNullOrWhiteSpace(Description) ? "none" : Description!);
        surface.Set("hint", Hint);

        surface.Class("fdesc", "empty", string.IsNullOrWhiteSpace(Description));

        foreach (string tone in new[] { "warn", "bad", "good" })
        {
            surface.Class("hint", tone, tone == HintTone);
        }

        foreach (string visibility in HudSlots.Visibilities)
        {
            surface.Class($"vis_{visibility}", "on", visibility == Visibility);
        }

        surface.Class("name", "asking", Asking == "name");
        surface.Class("desc", "asking", Asking == "desc");
    }
}
