using System.Text.Json;
using FiveStack;
using FiveStack.Entities;
using Xunit;

// The panel sends capitalised override keys ("Global", "Mode") and lowercased
// exec keys ("global", "mode"), and cfg_overrides deserializes with the default
// ordinal comparer. A case-sensitive lookup between the two silently drops every
// layer, which is a green-suite, nothing-applies failure -- so it is pinned here.
public class ConfigLayerTests
{
    private static MatchData Match(string payload) =>
        JsonSerializer.Deserialize<MatchData>(payload)!;

    private const string Layered =
        """
        {
          "is_lan": false,
          "options": {
            "type": "Competitive",
            "cfg_overrides": {
              "Competitive": "mp_freezetime 5",
              "Global": "mp_freezetime 3",
              "Plugin.inventory-simulator": "invsim_ws_enabled 1",
              "Mode": "mp_freezetime 1"
            },
            "cfg_execs": ["global", "plugin.inventory-simulator", "mode"]
          }
        }
        """;

    [Fact]
    public void Resolves_Lowercased_Exec_Keys_Against_Capitalised_Overrides()
    {
        Assert.Equal(
            new[] { "Competitive", "global", "plugin.inventory-simulator", "mode" },
            MatchManager.ConfigLayerKeys(Match(Layered))
        );
    }

    [Fact]
    public void Skips_A_Layer_With_No_Override_Text()
    {
        var match = Match(Layered);
        match.options.cfg_overrides["Global"] = "";

        Assert.DoesNotContain("global", MatchManager.ConfigLayerKeys(match));
    }

    [Fact]
    public void Refuses_A_Key_That_Would_Escape_The_Cfg_Filename()
    {
        var match = Match(Layered);
        match.options.cfg_execs = ["global\n", "../evil", "a/b", new string('x', 65)];

        Assert.Equal(new[] { "Competitive" }, MatchManager.ConfigLayerKeys(match));
    }

    // A panel older than cfg_execs still sends a mode's cvars as "Mode" and
    // expects them exec'd; without the fallback game modes stop applying the
    // moment this build rolls ahead of the API.
    [Fact]
    public void Falls_Back_To_The_Mode_Layer_When_The_Panel_Sends_No_Exec_List()
    {
        var match = Match(
            """
            {
              "is_lan": false,
              "options": {
                "type": "Competitive",
                "cfg_overrides": { "Mode": "mp_freezetime 1" }
              }
            }
            """
        );

        Assert.Equal(new[] { "Competitive", "mode" }, MatchManager.ConfigLayerKeys(match));
    }

    [Fact]
    public void Puts_Lan_Between_The_Type_And_The_Panel_Layers()
    {
        var match = Match(Layered);
        match.is_lan = true;

        Assert.Equal(
            new[] { "Competitive", "lan", "global", "plugin.inventory-simulator", "mode" },
            MatchManager.ConfigLayerKeys(match)
        );
    }
}
