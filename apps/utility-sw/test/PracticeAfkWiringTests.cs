using System.Text.RegularExpressions;
using Xunit;

// PracticeAfkTests prove the clock. What feeds it needs the engine, so these
// read the source and go red when a line that does that is lost.
public class PracticeAfkWiringTests
{
    private static string Code(params string[] path)
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            string candidate = Path.Combine(
                new[] { directory.FullName, "apps", "utility-sw", "src" }.Concat(path).ToArray()
            );

            if (File.Exists(candidate))
            {
                return Regex.Replace(File.ReadAllText(candidate), @"//[^\n]*", "");
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"{path[^1]} was not found above the test output");
    }

    private static string Method(string signature, params string[] file)
    {
        string source = Code(file);
        int from = source.IndexOf(signature, StringComparison.Ordinal);

        Assert.True(from >= 0, $"{signature} is gone");

        int to = source.IndexOf("\n    }\n", from, StringComparison.Ordinal);

        Assert.True(to > from, $"the end of {signature} was not found");

        return source[from..to];
    }

    [Fact]
    public void TalkingCountsAsActivity()
    {
        string wire = Method("private void WireAfk()", "Events", "PracticeAfkWatch.cs");
        string unwire = Method("private void UnwireAfk()", "Events", "PracticeAfkWatch.cs");

        Assert.Contains("Core.Event.OnClientVoice +=", wire);
        Assert.Contains("Core.Event.OnClientVoice -=", unwire);
        Assert.Matches(@"_voiceHandler\s*=\s*@?\w+\s*=>\s*Stir\(", wire);
    }

    [Fact]
    public void HeldButtonsCountOnlyForALivePawn()
    {
        string watch = Method("private void UseWatch()", "UtilityPracticePlugin.cs");

        Assert.Matches(@"if \(buttons != 0 && player\.IsAlive\)\s*\{\s*Stir\(player\.PlayerID\);", watch);
    }

    [Fact]
    public void ASpectatorIsJudgedByTheObserverPawnTheyLookThrough()
    {
        string sample = Method("private PracticeAfk.Sample AfkSample(IPlayer player)", "Events", "PracticeAfkWatch.cs");

        Assert.Contains("player.Pawn", sample);
        Assert.Contains("V_angle", sample);
        Assert.Contains("Alive: alive", sample);
    }

    [Fact]
    public void OnlyAFreeRoamingSpectatorIsJudgedByTheirView()
    {
        string sample = Method("private PracticeAfk.Sample AfkSample(IPlayer player)", "Events", "PracticeAfkWatch.cs");

        Assert.Contains("ObserverServices", sample);
        Assert.Contains("ObserverMode_t.OBS_MODE_ROAMING", sample);
        Assert.Contains("Roaming: roaming", sample);
    }

    [Fact]
    public void EveryDueStepIsTriedOnItsOwn()
    {
        string watch = Method("private void AfkWatch()", "Events", "PracticeAfkWatch.cs");

        Assert.Contains("PracticeAfk.Dispatch(", watch);
    }
}
