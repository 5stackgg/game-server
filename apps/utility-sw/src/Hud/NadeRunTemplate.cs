using FiveStack.Utilities;

namespace UtilityPractice;

// The execute as a list: who throws what, in what order, yours in colour and
// everyone else's greyed. Passive -- it is up while the round runs, so it must
// never take the cursor.
public class NadeRunTemplate : HudTemplate
{
    public record Step(
        string Time,
        string Name,
        string Who,
        string? Colour,
        bool Mine,
        bool Done,
        bool Now
    );

    private readonly List<Step> _steps = new();

    public override HudLayoutSlots Slots => HudSlots.Run;

    public required string Kicker { get; init; }
    public required string Title { get; init; }
    public required string Clock { get; init; }

    public NadeRunTemplate Add(Step step)
    {
        if (_steps.Count < HudSlots.RunSteps)
        {
            _steps.Add(step);
        }

        return this;
    }

    public override void Apply(HudSurface surface)
    {
        surface.Set("kicker", Kicker);
        surface.Set("title", Title);
        surface.Set("clock", Clock);

        for (int index = 0; index < HudSlots.RunSteps; index++)
        {
            int slot = index + 1;
            Step? step = index < _steps.Count ? _steps[index] : null;

            surface.Set($"st{slot}", step?.Time ?? "");
            surface.Set($"sn{slot}", step?.Name ?? "");
            surface.Set($"sy{slot}", step?.Who ?? "");
            surface.Class($"s{slot}", "hidden", step == null);

            if (step == null)
            {
                continue;
            }

            surface.Class($"s{slot}", "mine", step.Mine);
            surface.Class($"s{slot}", "theirs", !step.Mine);
            surface.Class($"s{slot}", "done", step.Done);
            surface.Class($"s{slot}", "now", step.Now);

            for (int colour = 0; colour < PracticeStepColors.Count; colour++)
            {
                string name = PracticeStepColors.For(colour).Name;

                surface.Class($"sw{slot}", name, name == step.Colour);
            }
        }
    }
}
