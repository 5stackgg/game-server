using System.Text.RegularExpressions;

namespace FiveStack.Utilities;

// Reads the shipped Panorama sources and reports where they and HudSlots have
// drifted apart. Regex rather than a parser: this checks a naming contract, it
// does not need to understand the markup.
public static class HudLayoutContract
{
    private static readonly Regex Variable = new Regex(
        @"\{s:([A-Za-z0-9_]+)\}",
        RegexOptions.Compiled
    );

    private static readonly Regex Button = new Regex(
        "<Button\\b[^>]*\\bid=\"([A-Za-z0-9_]+)\"",
        RegexOptions.Compiled
    );

    private static readonly Regex ElementId = new Regex(
        "\\bid=\"([A-Za-z0-9_]+)\"",
        RegexOptions.Compiled
    );

    public static SortedSet<string> Variables(string layoutXml)
    {
        return Collect(Variable, layoutXml);
    }

    public static SortedSet<string> Buttons(string layoutXml)
    {
        return Collect(Button, layoutXml);
    }

    public static SortedSet<string> ElementIds(string layoutXml)
    {
        return Collect(ElementId, layoutXml);
    }

    // The `#element.stateN` rules a stylesheet declares, e.g. every x0..x12 the
    // aim dot can be moved to.
    // byClass for grids shared by many elements (.nh-mx.x0), byId for grids
    // that belong to one (#aimx.x0).
    public static SortedSet<string> ClassStates(
        string css,
        string carrier,
        string prefix,
        bool byClass = false
    )
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        var rule = new Regex(
            $"{(byClass ? "\\." : "#")}{Regex.Escape(carrier)}\\.({Regex.Escape(prefix)}[A-Za-z0-9_]*)\\b"
        );

        foreach (Match match in rule.Matches(css))
        {
            found.Add(match.Groups[1].Value);
        }

        return found;
    }

    public static List<string> Verify(HudLayoutSlots declared, string layoutXml)
    {
        var problems = new List<string>();
        SortedSet<string> inLayout = Variables(layoutXml);
        var inCode = new SortedSet<string>(
            declared.Variables.Select(variable => variable.Name),
            StringComparer.Ordinal
        );

        foreach (string missing in inLayout.Except(inCode))
        {
            problems.Add($"{declared.Layout}: layout declares {{s:{missing}}}, HudSlots does not");
        }

        foreach (string missing in inCode.Except(inLayout))
        {
            problems.Add($"{declared.Layout}: HudSlots names '{missing}', layout has no {{s:{missing}}}");
        }

        SortedSet<string> buttonsInLayout = Buttons(layoutXml);
        var buttonsInCode = new SortedSet<string>(declared.Buttons, StringComparer.Ordinal);

        foreach (string missing in buttonsInLayout.Except(buttonsInCode))
        {
            problems.Add($"{declared.Layout}: layout has Button id='{missing}', HudSlots does not");
        }

        foreach (string missing in buttonsInCode.Except(buttonsInLayout))
        {
            problems.Add($"{declared.Layout}: HudSlots expects button '{missing}', layout has none");
        }

        if (!ElementIds(layoutXml).Contains(declared.RootId))
        {
            problems.Add($"{declared.Layout}: no element carries the root id '{declared.RootId}'");
        }

        // Each variable is written at its carrier, so that element must exist
        // and must be the one holding the {s:...} placeholder.
        foreach (HudVariable variable in declared.Variables)
        {
            IReadOnlyList<string> onElement = VariablesOn(layoutXml, variable.ElementId);

            if (onElement.Count == 0)
            {
                problems.Add(
                    $"{declared.Layout}: no element '{variable.ElementId}' to carry {{s:{variable.Name}}}"
                );
            }
            else if (!onElement.Contains(variable.Name))
            {
                problems.Add(
                    $"{declared.Layout}: element '{variable.ElementId}' renders "
                        + $"{{s:{string.Join(",", onElement)}}}, not {{s:{variable.Name}}}"
                );
            }
        }

        return problems;
    }

    // Every id the server toggles a class on has to exist, or the toggle is a
    // silent no-op.
    public static List<string> VerifyElements(
        HudLayoutSlots declared,
        string layoutXml,
        IEnumerable<string> elementIds
    )
    {
        SortedSet<string> present = ElementIds(layoutXml);

        return elementIds
            .Where(id => !present.Contains(id))
            .Select(id => $"{declared.Layout}: no element with id '{id}' to toggle classes on")
            .ToList();
    }

    // The {s:...} placeholders rendered by one element.
    public static IReadOnlyList<string> VariablesOn(string layoutXml, string elementId)
    {
        var element = new Regex($"<[A-Za-z]+\\b(?=[^>]*\\bid=\"{Regex.Escape(elementId)}\")[^>]*>");
        Match match = element.Match(layoutXml);

        return match.Success
            ? Variable.Matches(match.Value).Select(hit => hit.Groups[1].Value).ToList()
            : new List<string>();
    }

    // The classes an element carries in the markup, which is what a toggled
    // class has to be written against in the stylesheet.
    public static IReadOnlyList<string> ClassesOf(string layoutXml, string elementId)
    {
        var element = new Regex(
            $"<[A-Za-z]+\\b(?=[^>]*\\bid=\"{Regex.Escape(elementId)}\")[^>]*\\bclass=\"([^\"]*)\""
        );

        Match match = element.Match(layoutXml);

        return match.Success
            ? match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : new string[0];
    }

    // A class the server toggles that no rule selects is a write that changes
    // nothing on screen and reports no error.
    public static List<string> VerifyToggles(
        HudLayoutSlots declared,
        string layoutXml,
        string css,
        IEnumerable<(string ElementId, string Class)> toggles
    )
    {
        var problems = new List<string>();

        foreach ((string elementId, string name) in toggles)
        {
            IReadOnlyList<string> classes = ClassesOf(layoutXml, elementId);

            if (classes.Count == 0)
            {
                problems.Add($"{declared.Layout}: #{elementId} carries no class to qualify '{name}'");

                continue;
            }

            // Either scoping counts: a rule on the element's own id
            // (#radar.de_mirage) selects it just as surely as one on a class it
            // carries (.nh-row.selected).
            bool styled =
                new Regex($"#{Regex.Escape(elementId)}\\.{Regex.Escape(name)}\\b").IsMatch(css)
                || classes.Any(carried =>
                    new Regex($"\\.{Regex.Escape(carried)}\\.{Regex.Escape(name)}\\b").IsMatch(css)
                );

            if (!styled)
            {
                problems.Add(
                    $"{declared.Layout}: nothing styles '{name}' on #{elementId} "
                        + $"(expected .{classes[0]}.{name} in the stylesheet)"
                );
            }
        }

        return problems;
    }

    private static SortedSet<string> Collect(Regex pattern, string source)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Match match in pattern.Matches(source))
        {
            found.Add(match.Groups[1].Value);
        }

        return found;
    }
}
