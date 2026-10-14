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
    // A name that has been published to the Workshop is frozen. Servers still
    // on an older plugin keep asking the item for it, so changing the file
    // changes their HUD under them; and a client that has the item mounted
    // draws the published copy over a newer one of the same name, which is
    // how a changed layout fails to show up on a dev server. A layout or
    // stylesheet that changes after a publish gets a new name, and the old
    // file stays in hud/panorama until no supported plugin asks for it.
    //
    // nade_* and nadehud are the first published set. nade2_edit was published
    // drawing its buttons with classes nadehud2 never styled, and nade2_bar
    // with every compass pip the same grey.
    public const string Stylesheet = "nadehud2";

    // The wheel's own. It draws with things nothing else here uses, and a
    // stylesheet the game rejects takes every layout that includes it with it.
    public const string WheelStylesheet = "nadewheel2";

    // What the bar changed after nadehud2 was published. The bar includes it
    // after nadehud2, which is still the sheet its classes are checked against.
    public const string BarStylesheet = "nadebar3";

    public const string NadeHud = "nade2_hud";
    public const string NadePick = "nade2_pick";
    public const string NadeRun = "nade2_run";
    public const string NadeEdit = "nade3_edit";
    public const string NadeBar = "nade3_bar";
    public const string NadeWheel = "nade2_wheel";

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

    // Fixed DOM: this many markers exist in the picker's radar and no more. The
    // busiest targets win the slots.
    public const int MapMarkers = 40;

    // Shared .nh-mx/.nh-my class groups, so the grid costs 2 x MapGrid rules
    // rather than that many per marker.
    public const int MapGrid = 64;

    // Steps visible in the execute timeline; the panel scrolls past this.
    public const int RunSteps = 12;

    // The keys a throw can ask for, in the order they sit on the card.
    public static readonly IReadOnlyList<string> ThrowKeycaps =
        new[] { "shift", "ctrl", "w", "a", "s", "d", "jump" };

    // The command behind each of those, for the csgo_key formatter.
    public static string ThrowKeyBinding(string cap)
    {
        return cap switch
        {
            "shift" => "%sprint%",
            "ctrl" => "%duck%",
            "w" => "%forward%",
            "a" => "%left%",
            "s" => "%back%",
            "d" => "%right%",
            _ => "%jump%",
        };
    }

    // Fixed DOM, like the map's markers: the nearest spots win the pips.
    public const int CompassPips = 8;
    public const int CompassColumns = 256;
    public const float CompassHalfSpanDegrees = 60f;

    // TAB, E, R, then the three double taps and the clear key -- one row each,
    // in that order.
    public const int LegendKeys = 7;

    // The command behind each keycap, in the form CS2's csgo_key formatter
    // reads: it draws whatever key the player has bound to it.
    public static readonly IReadOnlyList<string> LegendBindings = new[]
    {
        "%showscores%",
        "%use%",
        "%reload%",
        "%duck%",
        "%sprint%",
        "%lookatweapon%",
        "%sellbackall%",
    };

    // Fixed DOM for the three lists the bar can show: what is out, the plan,
    // and what a flash did. The steps are how finely a bar can be drawn.
    public const int OutRows = 6;
    public const int OutFillSteps = 21;

    // Every kind of grenade, by RadarMaps' names for them.
    public static readonly IReadOnlyList<string> OutTypes = new[] { "smoke", "molly", "flash", "he", "decoy" };
    public const int PlanRows = 6;

    // The dots on the throw card: the last this many throws at its lineup.
    public const int LastThrows = 5;
    public const int FlashRows = 5;
    public const int FlashBarSteps = 21;

    // Wedges on the wheel, clockwise from the top. Fixed DOM again: the server
    // refills the same eight for whichever wheel is open.
    public const int WheelSlots = 8;

    // The game only hit-tests rectangles, so a wedge is covered by a stack of
    // invisible buttons. How many each one took, by slot; the layout is
    // generated and these are counted from it.
    public static readonly IReadOnlyList<int> WheelHits = new[] { 6, 7, 7, 7, 5, 7, 7, 7 };

    // The outer ring: two small quick actions over each wedge, clockwise, so
    // 2k and 2k + 1 sit over wedge k.
    public const int WheelQuicks = WheelSlots * 2;

    public static readonly IReadOnlyList<int> WheelQuickHits =
        new[] { 4, 4, 6, 7, 7, 7, 7, 6, 4, 4, 6, 7, 7, 7, 7, 6 };

    // One quick action on its own takes the whole width of its wedge, so
    // each wedge also has a segment that wide. It and the two halves are
    // never shown together.
    public static readonly IReadOnlyList<int> WheelWideHits =
        new[] { 4, 13, 17, 13, 4, 13, 17, 13 };

    public static readonly HudLayoutSlots Hud = new HudLayoutSlots(
        NadeHud,
        "NadeHud",
        new[]
        {
            new HudVariable("kicker", "kicker"),
            new HudVariable("pos", "pos"),
            new HudVariable("title", "title"),
            new HudVariable("tech", "tech"),
            new HudVariable("runup", "runup"),
            new HudVariable("spotval", "spotval"),
            new HudVariable("aimerr", "aimerr"),
            new HudVariable("drill", "drill"),
            new HudVariable("drillkey", "drillkey"),
            new HudVariable("kb_shift", "kc_shift"),
            new HudVariable("kb_ctrl", "kc_ctrl"),
            new HudVariable("kb_w", "kc_w"),
            new HudVariable("kb_a", "kc_a"),
            new HudVariable("kb_s", "kc_s"),
            new HudVariable("kb_d", "kc_d"),
            new HudVariable("kb_jump", "kc_jump"),
        },
        new string[0]
    );

    public static readonly HudLayoutSlots Pick = new HudLayoutSlots(
        NadePick,
        "NadePick",
        PickVariables().ToArray(),
        PickButtons().ToArray()
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

    public static readonly HudLayoutSlots Bar = new HudLayoutSlots(
        NadeBar,
        "NadeBar",
        BarVariables().ToArray(),
        new string[0]
    );

    public static readonly HudLayoutSlots Wheel = new HudLayoutSlots(
        NadeWheel,
        "NadeWheel",
        WheelVariables().ToArray(),
        WheelButtons().ToArray()
    );

    public static readonly IReadOnlyList<HudLayoutSlots> All =
        new[] { Hud, Pick, Run, Edit, Bar, Wheel };

    // The stylesheet a layout draws with, which is the one its classes have to
    // be checked against.
    public static string StylesheetFor(HudLayoutSlots slots)
    {
        return slots.Layout == NadeWheel ? WheelStylesheet : Stylesheet;
    }

    public static string WheelHit(int slot, int hit)
    {
        return $"r{slot + 1}h{hit + 1}";
    }

    public static string WheelQuickHit(int quick, int hit)
    {
        return $"o{quick + 1}h{hit + 1}";
    }

    public static string WheelWideHit(int slot, int hit)
    {
        return $"x{slot + 1}h{hit + 1}";
    }

    private static IEnumerable<HudVariable> BarVariables()
    {
        yield return new HudVariable("cleft", "cleft");
        yield return new HudVariable("cfocus", "cfocus");
        yield return new HudVariable("cright", "cright");

        for (int key = 1; key <= LegendKeys; key++)
        {
            yield return new HudVariable($"k{key}", $"k{key}");
            yield return new HudVariable($"kb{key}", $"kc{key}");
        }

        yield return new HudVariable("ocount", "ocount");
        yield return new HudVariable("omore", "omore");

        for (int row = 1; row <= OutRows; row++)
        {
            yield return new HudVariable($"on{row}", $"on{row}");
            yield return new HudVariable($"of{row}", $"of{row}");
            yield return new HudVariable($"os{row}", $"os{row}");
            yield return new HudVariable($"ot{row}", $"ot{row}");
        }

        yield return new HudVariable("pltitle", "pltitle");
        yield return new HudVariable("plsub", "plsub");
        yield return new HudVariable("plfoot", "plfoot");

        for (int row = 1; row <= PlanRows; row++)
        {
            yield return new HudVariable($"pl{row}", $"pl{row}");
            yield return new HudVariable($"plv{row}", $"plv{row}");
        }

        yield return new HudVariable("fltitle", "fltitle");
        yield return new HudVariable("flsub", "flsub");
        yield return new HudVariable("flfoot", "flfoot");

        for (int row = 1; row <= FlashRows; row++)
        {
            yield return new HudVariable($"fw{row}", $"fw{row}");
            yield return new HudVariable($"fs{row}", $"fs{row}");
            yield return new HudVariable($"fg{row}", $"fg{row}");
        }

        yield return new HudVariable("toast", "toast");
    }

    private static IEnumerable<HudVariable> WheelVariables()
    {
        for (int slot = 1; slot <= WheelSlots; slot++)
        {
            yield return new HudVariable($"w{slot}", $"w{slot}");
            yield return new HudVariable($"s{slot}", $"s{slot}");
        }

        for (int quick = 1; quick <= WheelQuicks; quick++)
        {
            yield return new HudVariable($"q{quick}", $"q{quick}");
        }

        for (int slot = 1; slot <= WheelSlots; slot++)
        {
            yield return new HudVariable($"xl{slot}", $"xl{slot}");
        }

        yield return new HudVariable("hubt", "hubt");
        yield return new HudVariable("hubs", "hubs");
    }

    private static IEnumerable<string> WheelButtons()
    {
        for (int slot = 0; slot < WheelSlots; slot++)
        {
            for (int hit = 0; hit < WheelHits[slot]; hit++)
            {
                yield return WheelHit(slot, hit);
            }
        }

        for (int quick = 0; quick < WheelQuicks; quick++)
        {
            for (int hit = 0; hit < WheelQuickHits[quick]; hit++)
            {
                yield return WheelQuickHit(quick, hit);
            }
        }

        for (int slot = 0; slot < WheelSlots; slot++)
        {
            for (int hit = 0; hit < WheelWideHits[slot]; hit++)
            {
                yield return WheelWideHit(slot, hit);
            }
        }

        yield return "hub";
        yield return "close";
    }

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

    private static IEnumerable<HudVariable> PickVariables()
    {
        yield return new HudVariable("title", "title");
        yield return new HudVariable("tag", "tag");
        yield return new HudVariable("page", "page");
        yield return new HudVariable("focus", "focus");

        // The buttons already own row1, so the labels inside them carry their
        // own ids.
        for (int row = 1; row <= ListRows; row++)
        {
            yield return new HudVariable($"row{row}", $"row{row}l");
            yield return new HudVariable($"row{row}v", $"row{row}v");
        }

        for (int marker = 1; marker <= MapMarkers; marker++)
        {
            yield return new HudVariable($"c{marker}", $"c{marker}");
            yield return new HudVariable($"n{marker}", $"n{marker}");
        }
    }

    private static IEnumerable<string> PickButtons()
    {
        for (int row = 1; row <= ListRows; row++)
        {
            yield return $"row{row}";
        }

        for (int marker = 1; marker <= MapMarkers; marker++)
        {
            yield return $"m{marker}";
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

        yield return "here";
        yield return "reset";
        yield return "prev";
        yield return "next";
        yield return "close";
    }
}
