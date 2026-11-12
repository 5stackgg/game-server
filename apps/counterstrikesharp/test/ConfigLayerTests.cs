using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    private static string SharedCfg(string file, [CallerFilePath] string testFile = "")
    {
        return File.ReadAllText(
            Path.Combine(Path.GetDirectoryName(testFile)!, "..", "..", "..", "shared", "cfg", file)
        );
    }

    private static string? CfgValue(string cfg, string cvar)
    {
        return cfg.Split('\n')
            .Select(line =>
                line.Split("//")[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            )
            .Where(tokens => tokens.Length >= 2 && tokens[0] == cvar)
            .Select(tokens => tokens[1])
            .LastOrDefault();
    }

    [Fact]
    public void Warmup_Protects_A_Respawned_Player()
    {
        Assert.Equal("4", CfgValue(SharedCfg("5stack.warmup.cfg"), "mp_respawn_immunitytime"));
    }

    // Nothing re-execs the type cfg between warmup and the knife round, so
    // the knife cfg is the only thing standing between it and warmup's value.
    [Fact]
    public void The_Knife_Round_Turns_Respawn_Immunity_Off()
    {
        Assert.Equal("-1", CfgValue(SharedCfg("5stack.knife.cfg"), "mp_respawn_immunitytime"));
    }

    [Theory]
    [InlineData("5stack.competitive.cfg")]
    [InlineData("5stack.wingman.cfg")]
    [InlineData("5stack.duel.cfg")]
    [InlineData("5stack.rush.cfg")]
    public void Every_Type_Cfg_Turns_Respawn_Immunity_Off(string file)
    {
        Assert.Equal("-1", CfgValue(SharedCfg(file), "mp_respawn_immunitytime"));
    }

    // An admin's type cfg replaces the shipped file outright, so it may not
    // set immunity back; the reset has to come before the layers so a mode
    // that wants spawn protection can still turn it on.
    [Theory]
    [InlineData("Competitive")]
    [InlineData("Rush")]
    public void Going_Live_Clears_Warmup_Respawn_Immunity_Before_The_Layers(string type)
    {
        var match = Match(Layered);
        match.options.type = type;
        match.options.cfg_overrides[type] = "mp_freezetime 5";

        string[] commands = MatchManager.MatchConfigExecCommands(match);

        int reset = Array.IndexOf(commands, "mp_respawn_immunitytime -1");
        int firstExec = Array.FindIndex(commands, command => command.StartsWith("exec "));

        Assert.True(reset >= 0, string.Join("; ", commands));
        Assert.True(reset < firstExec, string.Join("; ", commands));
    }

    // Every shipped type cfg sets mp_round_restart_delay, so the option only
    // sticks when it is sent after the execs.
    [Theory]
    [InlineData("Competitive")]
    [InlineData("Rush")]
    public void The_Round_Restart_Delay_Option_Lands_After_Every_Layer(string type)
    {
        var match = Match(Layered);
        match.options.type = type;
        match.options.cfg_overrides[type] = "mp_round_restart_delay 7";
        match.options.round_restart_delay = 3;

        string[] commands = MatchManager.MatchConfigExecCommands(match);

        int delay = Array.LastIndexOf(commands, "mp_round_restart_delay 3");
        int lastExec = Array.FindLastIndex(commands, command => command.StartsWith("exec "));

        Assert.True(lastExec >= 0, string.Join("; ", commands));
        Assert.True(delay > lastExec, string.Join("; ", commands));
    }

    [Fact]
    public void No_Round_Restart_Delay_Option_Leaves_The_Cfg_Value()
    {
        string[] commands = MatchManager.MatchConfigExecCommands(Match(Layered));

        Assert.DoesNotContain(commands, command => command.StartsWith("mp_round_restart_delay"));
    }

    private static string ServiceMethod(
        string file,
        string signature,
        [CallerFilePath] string testFile = ""
    )
    {
        string path = Path.Combine(
            Path.GetDirectoryName(testFile)!,
            "..",
            "src",
            "FiveStack.Services",
            file
        );
        string source = Regex.Replace(File.ReadAllText(path), @"//[^\n]*", "");
        int from = source.IndexOf(signature, StringComparison.Ordinal);

        Assert.True(from >= 0, $"{signature} is gone");

        int to = source.IndexOf("\n    }\n", from, StringComparison.Ordinal);

        Assert.True(to > from, $"the end of {signature} was not found");

        return source[from..to];
    }

    [Theory]
    [InlineData("MatchManager.cs", "public void SetupMatch(")]
    [InlineData("MatchManager.cs", "private void StartLive()")]
    [InlineData("KnifeSystem.cs", "public void SetWinningTeam(")]
    [InlineData("KnifeSystem.cs", "public void Skip()")]
    public void Every_Type_Cfg_Exec_Carries_The_Round_Restart_Delay(string file, string signature)
    {
        string method = ServiceMethod(file, signature);

        Assert.Contains("MatchConfigExecCommands()", method);
        Assert.DoesNotContain("mp_round_restart_delay", method);
    }
}
