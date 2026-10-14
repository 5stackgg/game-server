using FiveStack.Utilities;
using Xunit;

namespace FiveStack.Tests;

// Every failure mode of custom_hud_layout is silent: a variable the layout does
// not declare renders nothing and logs nothing. These are the only thing that
// catches a rename.
public class HudLayoutContractTests
{
    private static string HudRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "apps", "utility-sw", "hud");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("apps/utility-sw/hud not found above the test output");
    }

    private static string Layout(string name)
    {
        return File.ReadAllText(
            Path.Combine(HudRoot(), "panorama", "layout", "custom_game", $"{name}.xml")
        );
    }

    private static string Sheet(string name)
    {
        return File.ReadAllText(
            Path.Combine(HudRoot(), "panorama", "styles", "custom_game", $"{name}.css")
        );
    }

    private static string Styles()
    {
        return Sheet(HudSlots.Stylesheet);
    }

    private static string WheelStyles()
    {
        return Sheet(HudSlots.WheelStylesheet);
    }

    public static TheoryData<string> Layouts()
    {
        var data = new TheoryData<string>();

        foreach (HudLayoutSlots slots in HudSlots.All)
        {
            data.Add(slots.Layout);
        }

        return data;
    }

    // A layout that includes any other stylesheet draws with that one's rules:
    // the published set's, if it is the published name, and none at all if the
    // name is wrong.
    [Theory]
    [MemberData(nameof(Layouts))]
    public void LayoutIncludesTheStylesheetItIsTestedAgainst(string layout)
    {
        HudLayoutSlots slots = HudSlots.All.Single(candidate => candidate.Layout == layout);

        Assert.Contains(
            $"s2r://panorama/styles/custom_game/{HudSlots.StylesheetFor(slots)}.vcss_c",
            Layout(layout)
        );
    }

    // A malformed layout still gets most of the way through the panorama
    // compiler before failing with a position, not a reason. Catching it here
    // costs nothing and names the file.
    [Theory]
    [MemberData(nameof(Layouts))]
    public void LayoutIsWellFormedXml(string layout)
    {
        System.Xml.Linq.XDocument.Parse(Layout(layout));
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void LayoutAndCodeNameTheSameSlots(string layout)
    {
        HudLayoutSlots slots = HudSlots.All.Single(candidate => candidate.Layout == layout);

        Assert.Empty(HudLayoutContract.Verify(slots, Layout(layout)));
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void EveryClassALayoutUsesIsStyled(string layout)
    {
        HudLayoutSlots slots = HudSlots.All.Single(candidate => candidate.Layout == layout);

        Assert.Empty(
            HudLayoutContract.VerifyClasses(
                slots,
                Layout(layout),
                Sheet(HudSlots.StylesheetFor(slots))
            )
        );
    }

    [Fact]
    public void EveryElementTheHudTogglesClassesOnExists()
    {
        string[] toggled =
        {
            HudSlots.Hud.RootId,
            "nade",
            "fig",
            "spot",
            "aim",
            "drillrow",
            "drillbar",
            "mb1",
            "mb2",
            "runup",
            "ld1",
            "ld2",
            "ld3",
            "ld4",
            "ld5",
        };

        Assert.Empty(
            HudLayoutContract.VerifyElements(
                HudSlots.Hud,
                Layout(HudSlots.NadeHud),
                toggled.Concat(HudSlots.ThrowKeycaps.Select(cap => $"kc_{cap}"))
            )
        );
    }

    [Fact]
    public void EveryElementThePickerTogglesClassesOnExists()
    {
        var toggled = new List<string> { HudSlots.Pick.RootId, "pager", "mapcol", "radar", "here", "reset" };

        for (int row = 1; row <= HudSlots.ListRows; row++)
        {
            toggled.Add($"row{row}");
        }

        for (int marker = 1; marker <= HudSlots.MapMarkers; marker++)
        {
            toggled.Add($"m{marker}");
        }

        foreach (string side in HudSlots.ListSides)
        {
            toggled.Add($"side_{side}");
        }

        foreach (string scope in HudSlots.ListScopes)
        {
            toggled.Add($"scope_{scope}");
        }

        foreach (string type in HudSlots.ListTypes)
        {
            toggled.Add($"type_{type}");
        }

        Assert.Empty(
            HudLayoutContract.VerifyElements(HudSlots.Pick, Layout(HudSlots.NadePick), toggled)
        );
    }

    [Fact]
    public void EveryClassTheHudTogglesIsStyled()
    {
        var toggles = new List<(string, string)>
        {
            (HudSlots.Hud.RootId, HudSlots.Shown),
            (HudSlots.Hud.RootId, "go"),
            ("spot", "on"),
            ("aim", "on"),
            ("drillrow", HudSlots.Shown),
            ("mb1", "on"),
            ("mb2", "on"),
            ("runup", HudSlots.Shown),
        };

        for (int dot = 1; dot <= HudSlots.LastThrows; dot++)
        {
            toggles.Add(($"ld{dot}", "hit"));
            toggles.Add(($"ld{dot}", "miss"));
        }

        foreach (string cap in HudSlots.ThrowKeycaps)
        {
            toggles.Add(($"kc_{cap}", "on"));
        }

        foreach (string type in RadarMaps.Types)
        {
            toggles.Add(("nade", type));
        }

        foreach (string figure in PracticeThrowKeys.Figures)
        {
            toggles.Add(("fig", figure));
        }

        Assert.Empty(
            HudLayoutContract.VerifyToggles(HudSlots.Hud, Layout(HudSlots.NadeHud), Styles(), toggles)
        );
    }

    [Fact]
    public void EveryClassThePickerTogglesIsStyled()
    {
        var toggles = new List<(string, string)>
        {
            (HudSlots.Pick.RootId, HudSlots.Shown),
            ("pager", "hidden"),
            ("mapcol", "hidden"),
            ("here", "on"),
            ("reset", "hidden"),
        };

        for (int row = 1; row <= HudSlots.ListRows; row++)
        {
            toggles.Add(($"row{row}", "hidden"));
            toggles.Add(($"row{row}", "selected"));
        }

        foreach (string side in HudSlots.ListSides)
        {
            toggles.Add(($"side_{side}", "on"));
        }

        foreach (string scope in HudSlots.ListScopes)
        {
            toggles.Add(($"scope_{scope}", "on"));
        }

        foreach (string type in HudSlots.ListTypes)
        {
            toggles.Add(($"type_{type}", "on"));
        }

        foreach (string map in RadarMaps.All)
        {
            toggles.Add(("radar", map));
        }

        for (int marker = 1; marker <= HudSlots.MapMarkers; marker++)
        {
            toggles.Add(($"m{marker}", "hidden"));
            toggles.Add(($"m{marker}", "reachable"));
            toggles.Add(($"m{marker}", "loaded"));
            toggles.Add(($"m{marker}", "selected"));

            foreach (string type in RadarMaps.Types)
            {
                toggles.Add(($"m{marker}", type));
            }
        }

        Assert.Empty(
            HudLayoutContract.VerifyToggles(
                HudSlots.Pick,
                Layout(HudSlots.NadePick),
                Styles(),
                toggles
            )
        );
    }

    // The marker grid is shared across every marker (.nh-mx / .nh-my) rather
    // than scoped per id, so it is checked by class not by element.
    [Theory]
    [InlineData("nh-marker", "x")]
    [InlineData("nh-marker", "y")]
    public void MarkerGridIsDeclaredForEveryCell(string carrier, string prefix)
    {
        SortedSet<string> declared = HudLayoutContract.ClassStates(Styles(), carrier, prefix, byClass: true);
        var expected = new SortedSet<string>(
            Enumerable.Range(0, HudSlots.MapGrid).Select(step => $"{prefix}{step}"),
            StringComparer.Ordinal
        );

        Assert.Equal(expected, declared);
    }

    private static List<string> BarElements()
    {
        var elements = new List<string>
        {
            HudSlots.Bar.RootId,
            "compass",
            "legend",
            "toast",
        };

        for (int pip = 1; pip <= HudSlots.CompassPips; pip++)
        {
            elements.Add($"p{pip}");
        }

        for (int key = 1; key <= HudSlots.LegendKeys; key++)
        {
            elements.Add($"kr{key}");
        }

        elements.Add("out");
        elements.Add("plan");
        elements.Add("flash");

        for (int row = 1; row <= HudSlots.OutRows; row++)
        {
            elements.Add($"or{row}");
            elements.Add($"ob{row}");
        }

        for (int row = 1; row <= HudSlots.PlanRows; row++)
        {
            elements.Add($"plr{row}");
        }

        for (int row = 1; row <= HudSlots.FlashRows; row++)
        {
            elements.Add($"fr{row}");
            elements.Add($"fa{row}");
            elements.Add($"fu{row}");
        }

        return elements;
    }

    [Fact]
    public void EveryElementTheBarTogglesClassesOnExists()
    {
        Assert.Empty(
            HudLayoutContract.VerifyElements(HudSlots.Bar, Layout(HudSlots.NadeBar), BarElements())
        );
    }

    [Fact]
    public void EveryClassTheBarTogglesIsStyled()
    {
        var toggles = new List<(string, string)>
        {
            (HudSlots.Bar.RootId, HudSlots.Shown),
            ("compass", "hidden"),
            ("legend", "hidden"),
            ("toast", HudSlots.Shown),
        };

        for (int pip = 1; pip <= HudSlots.CompassPips; pip++)
        {
            toggles.Add(($"p{pip}", "hidden"));
            toggles.Add(($"p{pip}", "focus"));

            foreach (string type in RadarMaps.Types)
            {
                toggles.Add(($"p{pip}", type));
            }
        }

        for (int key = 1; key <= HudSlots.LegendKeys; key++)
        {
            toggles.Add(($"kr{key}", "hidden"));
            toggles.Add(($"kr{key}", "on"));
            toggles.Add(($"kr{key}", "dim"));
        }

        toggles.Add(("out", HudSlots.Shown));
        toggles.Add(("plan", HudSlots.Shown));
        toggles.Add(("flash", HudSlots.Shown));
        toggles.Add(("flash", "fire"));

        for (int row = 1; row <= HudSlots.OutRows; row++)
        {
            toggles.Add(($"or{row}", HudSlots.Shown));
            toggles.Add(($"or{row}", "air"));
            toggles.Add(($"or{row}", "fade"));

            foreach (string type in HudSlots.OutTypes)
            {
                toggles.Add(($"or{row}", type));
            }

            for (int color = 0; color < PracticeStepColors.Count; color++)
            {
                toggles.Add(($"or{row}", PracticeStepColors.For(color).Name));
            }

            for (int step = 0; step < HudSlots.OutFillSteps; step++)
            {
                toggles.Add(($"ob{row}", $"f{step}"));
            }
        }

        for (int row = 1; row <= HudSlots.PlanRows; row++)
        {
            toggles.Add(($"plr{row}", HudSlots.Shown));
            toggles.Add(($"plr{row}", "now"));
            toggles.Add(($"plr{row}", "done"));
        }

        for (int row = 1; row <= HudSlots.FlashRows; row++)
        {
            toggles.Add(($"fr{row}", HudSlots.Shown));
            toggles.Add(($"fr{row}", "bad"));

            for (int step = 0; step < HudSlots.FlashBarSteps; step++)
            {
                toggles.Add(($"fa{row}", $"d{step}"));
                toggles.Add(($"fu{row}", $"d{step}"));
            }
        }

        Assert.Empty(
            HudLayoutContract.VerifyToggles(
                HudSlots.Bar,
                Layout(HudSlots.NadeBar),
                Styles(),
                toggles
            )
        );
    }

    // A pip sent to a cell with no rule keeps the margin of the cell it left,
    // and sits on the ribbon pointing at nothing.
    [Fact]
    public void TheCompassHasACellForEveryColumn()
    {
        SortedSet<string> declared = HudLayoutContract.ClassStates(
            Styles(),
            "nh-pip",
            "x",
            byClass: true
        );

        var expected = new SortedSet<string>(
            Enumerable.Range(0, HudSlots.CompassColumns).Select(step => $"x{step}"),
            StringComparer.Ordinal
        );

        Assert.Equal(expected, declared);
    }

    // The bar's sheet overrides nadehud2, which only works if it comes after.
    [Fact]
    public void TheBarIncludesItsOwnSheetAfterTheSharedOne()
    {
        string layout = Layout(HudSlots.NadeBar);
        int shared = layout.IndexOf($"/{HudSlots.Stylesheet}.vcss_c", StringComparison.Ordinal);
        int own = layout.IndexOf($"/{HudSlots.BarStylesheet}.vcss_c", StringComparison.Ordinal);

        Assert.True(shared >= 0 && own > shared);
    }

    // A type with no colour of its own draws in the grey every pip used to be.
    [Fact]
    public void EveryCompassTypeHasItsOwnColour()
    {
        string sheet = Sheet(HudSlots.BarStylesheet);
        var colours = new HashSet<string>(StringComparer.Ordinal);

        foreach (string type in RadarMaps.Types)
        {
            var rule = System.Text.RegularExpressions.Regex.Match(
                sheet,
                $@"\.nh-pip\.{type}\s*\{{\s*wash-color:\s*([^;]+);"
            );

            Assert.True(rule.Success, $"nothing colours a {type} pip");
            colours.Add(rule.Groups[1].Value);
        }

        Assert.Equal(RadarMaps.Types.Count, colours.Count);
    }

    [Fact]
    public void EveryClassTheWheelTogglesIsStyled()
    {
        var toggles = new List<(string, string)> { (HudSlots.Wheel.RootId, HudSlots.Shown) };

        for (int slot = 1; slot <= HudSlots.WheelSlots; slot++)
        {
            foreach (string state in new[] { "blank", "cat", "on", "pick", "disabled" })
            {
                toggles.Add(($"r{slot}", state));
            }
        }

        for (int quick = 1; quick <= HudSlots.WheelQuicks; quick++)
        {
            foreach (string state in new[] { "blank", "on", "disabled" })
            {
                toggles.Add(($"o{quick}", state));
            }
        }

        for (int slot = 1; slot <= HudSlots.WheelSlots; slot++)
        {
            foreach (string state in new[] { "blank", "on", "disabled" })
            {
                toggles.Add(($"x{slot}", state));
            }
        }

        Assert.Empty(
            HudLayoutContract.VerifyToggles(
                HudSlots.Wheel,
                Layout(HudSlots.NadeWheel),
                WheelStyles(),
                toggles
            )
        );
    }

    // A hit rectangle without a place sits in the top left corner at no size,
    // and a wedge without its slice is the whole ring: every wedge would light
    // at once.
    [Fact]
    public void EveryWedgeHasItsSliceItsLabelAndItsHitRectangles()
    {
        string styles = WheelStyles();

        for (int slot = 0; slot < HudSlots.WheelSlots; slot++)
        {
            Assert.Matches($@"\.nw-k{slot} \.nw-fill, \.nw-k{slot} \.nw-rim, \.nw-k{slot} \.nw-lit \{{ clip: radial\(", styles);
            Assert.Matches($@"\.nw-k{slot} \.nw-text \{{ margin-left: -?\d+px; margin-top: -?\d+px; \}}", styles);

            for (int hit = 0; hit < HudSlots.WheelHits[slot]; hit++)
            {
                string id = HudSlots.WheelHit(slot, hit);

                Assert.Matches($@"#{id} \{{ margin-left: \d+px; margin-top: \d+px; width: \d+px; height: \d+px; \}}", styles);
                Assert.Matches($@"#{id} \.nw-lit \{{ margin-left: -\d+px; margin-top: -\d+px; \}}", styles);
            }
        }

        for (int slot = 0; slot < HudSlots.WheelSlots; slot++)
        {
            Assert.Matches($@"\.nw-x{slot} \.nw-qfill, \.nw-x{slot} \.nw-qedge, \.nw-x{slot} \.nw-qlit \{{ clip: radial\(", styles);
            Assert.Matches($@"\.nw-x{slot} \.nw-qlabel \{{ margin-left: -?\d+px; margin-top: -?\d+px; \}}", styles);

            for (int hit = 0; hit < HudSlots.WheelWideHits[slot]; hit++)
            {
                string id = HudSlots.WheelWideHit(slot, hit);

                Assert.Matches($@"#{id} \{{ margin-left: \d+px; margin-top: \d+px; width: \d+px; height: \d+px; \}}", styles);
                Assert.Matches($@"#{id} \.nw-qlit \{{ margin-left: -\d+px; margin-top: -\d+px; \}}", styles);
            }
        }

        for (int quick = 0; quick < HudSlots.WheelQuicks; quick++)
        {
            Assert.Matches($@"\.nw-q{quick} \.nw-qfill, \.nw-q{quick} \.nw-qedge, \.nw-q{quick} \.nw-qlit \{{ clip: radial\(", styles);
            Assert.Matches($@"\.nw-q{quick} \.nw-qlabel \{{ margin-left: -?\d+px; margin-top: -?\d+px; \}}", styles);

            for (int hit = 0; hit < HudSlots.WheelQuickHits[quick]; hit++)
            {
                string id = HudSlots.WheelQuickHit(quick, hit);

                Assert.Matches($@"#{id} \{{ margin-left: \d+px; margin-top: \d+px; width: \d+px; height: \d+px; \}}", styles);
                Assert.Matches($@"#{id} \.nw-qlit \{{ margin-left: -\d+px; margin-top: -\d+px; \}}", styles);
            }
        }
    }

    // The slices are cut from a ring by angle, so eight of them have to turn
    // the whole way round between them with no two starting in the same place.
    [Fact]
    public void TheWedgesAreCutAnEighthOfATurnApart()
    {
        var starts = System.Text.RegularExpressions.Regex
            .Matches(WheelStyles(), @"\.nw-k\d \.nw-fill,[^{]*\{ clip: radial\( 50% 50%, ([0-9.]+)deg, ([0-9.]+)deg \);")
            .Select(match => (
                Start: float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                Sweep: float.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)
            ))
            .ToList();

        Assert.Equal(HudSlots.WheelSlots, starts.Count);
        Assert.Single(starts.Select(entry => entry.Sweep).Distinct());
        Assert.True(starts[0].Sweep < 360f / HudSlots.WheelSlots);

        List<float> ordered = starts.Select(entry => entry.Start).OrderBy(start => start).ToList();

        for (int index = 1; index < ordered.Count; index++)
        {
            Assert.Equal(360f / HudSlots.WheelSlots, ordered[index] - ordered[index - 1], 2);
        }
    }

    // The wheel uses things nothing else here does. If the game ever rejects
    // them, only a stylesheet the wheel alone includes goes with it.
    [Fact]
    public void OnlyTheWheelIncludesTheWheelStylesheet()
    {
        foreach (HudLayoutSlots slots in HudSlots.All)
        {
            bool includes = Layout(slots.Layout).Contains($"{HudSlots.WheelStylesheet}.vcss_c");

            Assert.Equal(slots.Layout == HudSlots.NadeWheel, includes);
        }

        Assert.DoesNotContain("clip:", Styles());
    }

    // A figure the stylesheet names but the addon does not carry draws as
    // nothing, with no error anywhere.
    [Fact]
    public void EveryFigureTheBarCanShowIsADrawingInTheAddon()
    {
        string images = Path.Combine(HudRoot(), "panorama", "images", "custom_game");
        string styles = Styles();

        foreach (string figure in PracticeThrowKeys.Figures)
        {
            Assert.True(
                File.Exists(Path.Combine(images, $"nade2_fig_{figure}.svg")),
                $"no drawing for {figure}"
            );
            Assert.Contains($"custom_game/nade2_fig_{figure}.vsvg", styles);
        }

        Assert.Equal(
            Enum.GetNames<FiveStack.Enums.eThrowTechnique>().Select(name => name.ToLowerInvariant()).OrderBy(name => name),
            PracticeThrowKeys.Figures.OrderBy(name => name)
        );
    }

    // A map with an image but no calibration (or vice versa) draws markers in
    // the wrong place, which is worse than drawing none.
    // The grenade icon and the bar's edge take their classes straight from
    // PracticeStepColors, so a colour added there without a rule here would
    // leave both the colour of the last throw.
    [Fact]
    public void EveryStepColourIsStyled()
    {
        string styles = Styles();
        var toggles = new List<(string, string)>();

        for (int index = 0; index < PracticeStepColors.Count; index++)
        {
            toggles.Add(("nade", PracticeStepColors.For(index).Name));
            toggles.Add((HudSlots.Hud.RootId, PracticeStepColors.For(index).Name));
        }

        Assert.Empty(
            HudLayoutContract.VerifyToggles(HudSlots.Hud, Layout(HudSlots.NadeHud), styles, toggles)
        );
    }

    // Green and red are the reticle's ramp. A step colour wearing either would
    // read as aim feedback; PracticeStepColors asserts the palette, this asserts
    // the HUD never styles one in.
    [Fact]
    public void NoStepColourCollidesWithTheReticle()
    {
        for (int index = 0; index < PracticeStepColors.Count; index++)
        {
            string name = PracticeStepColors.For(index).Name;

            Assert.NotEqual("green", name);
            Assert.NotEqual("red", name);
        }
    }

    [Fact]
    public void EveryRadarMapHasAnImageAStyleAndCalibration()
    {
        string styles = Styles();
        var metadata = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(HudRoot(), "radars", "metadata.json"))
        );

        foreach (string map in RadarMaps.All)
        {
            Assert.Contains($"#radar.{map}", styles);
            Assert.True(
                metadata.RootElement.TryGetProperty(map, out _),
                $"{map}: no calibration in metadata.json"
            );
        }
    }

    [Fact]
    public void EveryElementTheRunTogglesClassesOnExists()
    {
        var toggled = new List<string> { HudSlots.Run.RootId };

        for (int step = 1; step <= HudSlots.RunSteps; step++)
        {
            toggled.Add($"s{step}");
            toggled.Add($"sw{step}");
        }

        Assert.Empty(
            HudLayoutContract.VerifyElements(HudSlots.Run, Layout(HudSlots.NadeRun), toggled)
        );
    }

    [Fact]
    public void EveryClassTheRunTogglesIsStyled()
    {
        var toggles = new List<(string, string)> { (HudSlots.Run.RootId, HudSlots.Shown) };

        for (int step = 1; step <= HudSlots.RunSteps; step++)
        {
            foreach (string state in new[] { "hidden", "mine", "theirs", "done", "now" })
            {
                toggles.Add(($"s{step}", state));
            }

            for (int colour = 0; colour < PracticeStepColors.Count; colour++)
            {
                toggles.Add(($"sw{step}", PracticeStepColors.For(colour).Name));
            }
        }

        Assert.Empty(
            HudLayoutContract.VerifyToggles(HudSlots.Run, Layout(HudSlots.NadeRun), Styles(), toggles)
        );
    }

    [Fact]
    public void EveryElementTheEditTogglesClassesOnExists()
    {
        var toggled = new List<string> { HudSlots.Edit.RootId, "name", "desc", "fdesc", "hint" };

        foreach (string visibility in HudSlots.Visibilities)
        {
            toggled.Add($"vis_{visibility}");
        }

        Assert.Empty(
            HudLayoutContract.VerifyElements(HudSlots.Edit, Layout(HudSlots.NadeEdit), toggled)
        );
    }

    [Fact]
    public void EveryClassTheEditTogglesIsStyled()
    {
        var toggles = new List<(string, string)>
        {
            (HudSlots.Edit.RootId, HudSlots.Shown),
            ("name", "asking"),
            ("desc", "asking"),
            ("fdesc", "empty"),
            ("hint", "warn"),
            ("hint", "bad"),
            ("hint", "good"),
        };

        foreach (string visibility in HudSlots.Visibilities)
        {
            toggles.Add(($"vis_{visibility}", "on"));
        }

        Assert.Empty(
            HudLayoutContract.VerifyToggles(
                HudSlots.Edit,
                Layout(HudSlots.NadeEdit),
                Styles(),
                toggles
            )
        );
    }

    // Geometry must not be editable: a lineup that moves keeps its id, so every
    // scored attempt against it silently becomes a measurement of a different
    // throw.
    [Fact]
    public void TheEditPanelCannotTouchGeometry()
    {
        string layout = Layout(HudSlots.NadeEdit);

        foreach (string forbidden in new[] { "origin", "yaw", "pitch", "land", "velocity", "throw" })
        {
            Assert.DoesNotContain(forbidden, layout, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void RootsAreCollapsedUntilAPlayerIsOptedIn()
    {
        string styles = Styles();

        Assert.Contains(".nh-root.shown", styles);
        Assert.Contains(".nh-pick.shown", styles);
        Assert.Contains(".nh-bar.shown", styles);
        Assert.Contains(".nw-wheel.shown", WheelStyles());
    }
}
