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

    private static string Styles()
    {
        return File.ReadAllText(
            Path.Combine(HudRoot(), "panorama", "styles", "custom_game", "nadehud.css")
        );
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

    [Fact]
    public void EveryElementTheHudTogglesClassesOnExists()
    {
        string[] toggled =
        {
            HudSlots.Hud.RootId,
            "spot",
            "aim",
            "aimerr",
            "noterow",
            "drillrow",
            "drillbar",
        };

        Assert.Empty(HudLayoutContract.VerifyElements(HudSlots.Hud, Layout(HudSlots.NadeHud), toggled));
    }

    [Fact]
    public void EveryElementTheListTogglesClassesOnExists()
    {
        var toggled = new List<string> { HudSlots.List.RootId, "pager" };

        for (int row = 1; row <= HudSlots.ListRows; row++)
        {
            toggled.Add($"row{row}");
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

        toggled.Add("body");

        Assert.Empty(
            HudLayoutContract.VerifyElements(HudSlots.List, Layout(HudSlots.NadeList), toggled)
        );
    }


    [Fact]
    public void EveryClassTheHudTogglesIsStyled()
    {
        var toggles = new List<(string, string)>
        {
            (HudSlots.Hud.RootId, HudSlots.Shown),
            (HudSlots.Hud.RootId, "docked"),
            ("spot", "on"),
            ("aim", "on"),
            ("aimerr", "on"),
            ("steer", "on"),
            ("throwcolor", "hidden"),
            ("noterow", HudSlots.Shown),
            ("drillrow", HudSlots.Shown),
        };

        Assert.Empty(
            HudLayoutContract.VerifyToggles(HudSlots.Hud, Layout(HudSlots.NadeHud), Styles(), toggles)
        );
    }

    [Fact]
    public void EveryClassTheListTogglesIsStyled()
    {
        var toggles = new List<(string, string)>
        {
            (HudSlots.List.RootId, HudSlots.Shown),
            ("pager", "hidden"),
        };

        for (int row = 1; row <= HudSlots.ListRows; row++)
        {
            toggles.Add(($"row{row}", "hidden"));
            toggles.Add(($"row{row}", "selected"));
            toggles.Add(($"row{row}", "disabled"));
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


        Assert.Empty(
            HudLayoutContract.VerifyToggles(
                HudSlots.List,
                Layout(HudSlots.NadeList),
                Styles(),
                toggles
            )
        );
    }

    [Fact]
    public void EveryElementTheMapTogglesClassesOnExists()
    {
        var toggled = new List<string> { HudSlots.Map.RootId, "radar", "detail", "list" };

        for (int marker = 1; marker <= HudSlots.MapMarkers; marker++)
        {
            toggled.Add($"m{marker}");
            toggled.Add($"n{marker}");
        }

        Assert.Empty(
            HudLayoutContract.VerifyElements(HudSlots.Map, Layout(HudSlots.NadeMap), toggled)
        );
    }

    [Fact]
    public void EveryClassTheMapTogglesIsStyled()
    {
        var toggles = new List<(string, string)>
        {
            (HudSlots.Map.RootId, HudSlots.Shown),
            ("detail", HudSlots.Shown),
            ("list", "hidden"),
        };

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
                HudSlots.Map,
                Layout(HudSlots.NadeMap),
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

    // A map with an image but no calibration (or vice versa) draws markers in
    // the wrong place, which is worse than drawing none.
    // The chip's classes come straight from PracticeStepColors, so a colour
    // added there without a rule here would render an unstyled pill.
    [Fact]
    public void EveryStepColourIsStyled()
    {
        string styles = Styles();
        var toggles = new List<(string, string)>();

        for (int index = 0; index < PracticeStepColors.Count; index++)
        {
            toggles.Add(("throwcolor", PracticeStepColors.For(index).Name));
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
        Assert.Contains(".nh-list.shown", styles);
    }
}
