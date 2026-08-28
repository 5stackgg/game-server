namespace FiveStack.Utilities;

// The contract between a compiled Panorama layout and the C# that drives it.
// Naming a variable the layout does not declare renders nothing and throws
// nothing, so every name a template is allowed to write lives here and is
// asserted against the shipped .xml by HudLayoutContractTests.
// A dialog variable is addressed at the element that renders it, not at the
// layout root -- root propagation is a Panorama behaviour we would be assuming,
// and addressing the carrier is correct either way.
public record HudVariable(string Name, string ElementId);

public class HudLayoutSlots
{
    public HudLayoutSlots(
        string layout,
        string rootId,
        IReadOnlyList<HudVariable> variables,
        IReadOnlyList<string> buttons
    )
    {
        Layout = layout;
        RootId = rootId;
        Variables = variables;
        Buttons = buttons;
    }

    public string Layout { get; }
    public string RootId { get; }
    public IReadOnlyList<HudVariable> Variables { get; }
    public IReadOnlyList<string> Buttons { get; }

    public string ElementFor(string variable)
    {
        foreach (HudVariable candidate in Variables)
        {
            if (candidate.Name == variable)
            {
                return candidate.ElementId;
            }
        }

        throw new ArgumentException($"{Layout} declares no variable '{variable}'");
    }
}

public static class HudSlots
{
    public const string NadeHud = "nade_hud";
    public const string NadeList = "nade_list";
    public const string NadeMap = "nade_map";
    public const string NadeRun = "nade_run";
    public const string NadeEdit = "nade_edit";

    // Class toggled on a layout's root to show it for one player. The layouts
    // are collapsed by default so a spawned entity is invisible until somebody
    // is opted in.
    public const string Shown = "shown";

    public const int MeterSteps = 11;

    public const int ListRows = 16;
    // Filter chips, in panel. Each is a button id and a class-toggled element.
    public static readonly IReadOnlyList<string> ListSides = new[] { "all", "t", "ct" };
    // Mirrors the panel's scope filter. "favorites" is deliberately absent: the
    // library payload carries no favourite flag, and a chip that silently
    // matches nothing is worse than no chip.
    public static readonly IReadOnlyList<string> ListScopes =
        new[] { "all", "mine", "team", "public" };

    public static readonly IReadOnlyList<string> ListTypes =
        new[] { "all", "smoke", "flash", "molly", "he" };

    // Fixed DOM: this many markers exist in nade_map.xml and no more. The busiest
    // targets win the slots.
    public const int MapMarkers = 40;

    // Shared .nh-mx/.nh-my class groups, so the grid costs 2 x MapGrid rules
    // rather than that many per marker.
    public const int MapGrid = 64;

    // Steps visible in the execute timeline; the panel scrolls past this.
    public const int RunSteps = 12;

    public static readonly HudLayoutSlots Hud = new HudLayoutSlots(
        NadeHud,
        "NadeHud",
        new[]
        {
            new HudVariable("kicker", "kicker"),
            new HudVariable("pos", "pos"),
            new HudVariable("title", "title"),
            new HudVariable("tech", "tech"),
            new HudVariable("aimerr", "aimerr"),
            new HudVariable("steer", "steer"),
            new HudVariable("throwcolor", "throwcolor"),
            new HudVariable("drill", "drill"),
        },
        new string[0]
    );

    public static readonly HudLayoutSlots List = new HudLayoutSlots(
        NadeList,
        "NadeList",
        Rows().ToArray(),
        Buttons().ToArray()
    );

    public static readonly HudLayoutSlots Map = new HudLayoutSlots(
        NadeMap,
        "NadeMap",
        MapVariables().ToArray(),
        MapButtons().ToArray()
    );

    public static readonly HudLayoutSlots Run = new HudLayoutSlots(
        NadeRun,
        "NadeRun",
        RunVariables().ToArray(),
        new string[0]
    );

    // Only what utility-lineups.service.ts already knows how to UPDATE. Geometry
    // is deliberately absent.
    public static readonly IReadOnlyList<string> Visibilities =
        new[] { "private", "team", "public" };

    public static readonly HudLayoutSlots Edit = new HudLayoutSlots(
        NadeEdit,
        "NadeEdit",
        new[]
        {
            new HudVariable("tag", "tag"),
            new HudVariable("title", "title"),
            new HudVariable("fname", "fname"),
            new HudVariable("fdesc", "fdesc"),
            new HudVariable("hint", "hint"),
        },
        EditButtons().ToArray()
    );

    public static readonly IReadOnlyList<HudLayoutSlots> All =
        new[] { Hud, List, Map, Run, Edit };

    private static IEnumerable<string> EditButtons()
    {
        yield return "name";
        yield return "desc";

        foreach (string visibility in Visibilities)
        {
            yield return $"vis_{visibility}";
        }

        yield return "save";
        yield return "revert";
        yield return "close";
    }

    private static IEnumerable<HudVariable> RunVariables()
    {
        yield return new HudVariable("kicker", "kicker");
        yield return new HudVariable("title", "title");
        yield return new HudVariable("clock", "clock");

        for (int step = 1; step <= RunSteps; step++)
        {
            yield return new HudVariable($"st{step}", $"st{step}");
            yield return new HudVariable($"sn{step}", $"sn{step}");
            yield return new HudVariable($"sy{step}", $"sy{step}");
        }
    }

    private static IEnumerable<HudVariable> MapVariables()
    {
        yield return new HudVariable("title", "title");
        yield return new HudVariable("tag", "tag");
        yield return new HudVariable("focus", "focus");
        yield return new HudVariable("dname", "dname");
        yield return new HudVariable("dmeta", "dmeta");
        yield return new HudVariable("dload", "dload");
        yield return new HudVariable("dlist", "dlist");

        for (int marker = 1; marker <= MapMarkers; marker++)
        {
            yield return new HudVariable($"c{marker}", $"c{marker}");
            yield return new HudVariable($"n{marker}", $"n{marker}");
        }
    }

    private static IEnumerable<string> MapButtons()
    {
        for (int marker = 1; marker <= MapMarkers; marker++)
        {
            yield return $"m{marker}";
        }

        yield return "load";
        yield return "list";
        yield return "close";
    }

    private static IEnumerable<HudVariable> Rows()
    {
        yield return new HudVariable("title", "title");
        yield return new HudVariable("tag", "tag");
        yield return new HudVariable("page", "page");

        // The buttons already own row1/tab1, so the labels inside them carry
        // their own ids.
        for (int row = 1; row <= ListRows; row++)
        {
            yield return new HudVariable($"row{row}", $"row{row}l");
            yield return new HudVariable($"row{row}v", $"row{row}v");
        }

    }

    private static IEnumerable<string> Buttons()
    {
        for (int row = 1; row <= ListRows; row++)
        {
            yield return $"row{row}";
        }

        foreach (string side in ListSides)
        {
            yield return $"side_{side}";
        }

        foreach (string scope in ListScopes)
        {
            yield return $"scope_{scope}";
        }

        foreach (string type in ListTypes)
        {
            yield return $"type_{type}";
        }

        yield return "prev";
        yield return "next";
        yield return "close";
    }
}
