using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

public class TeamPlacementTests
{
    private static readonly string[] BuildOutput = ["bin", "obj", "build"];

    private static string SourceDir([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "src"));
    }

    // CS2 spawns a player the plugin puts on a team. Every under-the-map spawn
    // in match 46401241 came from the plugin respawning them itself (#241).
    // The one exception is a player still dead in warmup after the team change.
    [Fact]
    public void ThePluginOnlyRespawnsAPlayerStuckDeadInWarmup()
    {
        string src = SourceDir();

        Assert.True(Directory.Exists(src), $"plugin source not found at {src}");

        List<string> respawns = Directory
            .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(file =>
                !Path.GetRelativePath(src, file)
                    .Split(Path.DirectorySeparatorChar)
                    .Any(BuildOutput.Contains)
            )
            .SelectMany(file =>
                File.ReadLines(file)
                    .Select((line, index) => (file, line, number: index + 1))
            )
            .Where(entry => Regex.IsMatch(entry.line, @"\.Respawn(Async)?\s*\("))
            .Select(entry => $"{Path.GetRelativePath(src, entry.file)}:{entry.number}")
            .ToList();

        Assert.Single(respawns);

        string matchManager = File.ReadAllText(
            Path.Combine(src, "FiveStack.Services", "MatchManager.cs")
        );
        int from = matchManager.IndexOf(
            "private void RespawnIfStuckInWarmup(",
            StringComparison.Ordinal
        );

        Assert.True(from >= 0, "RespawnIfStuckInWarmup is gone");

        string method = matchManager[from..matchManager.IndexOf("\n    }\n", from, StringComparison.Ordinal)];

        Assert.StartsWith(
            Path.Combine("FiveStack.Services", "MatchManager.cs") + ":",
            respawns[0]
        );
        Assert.Matches(@"\.Respawn\s*\(", method);
        Assert.Contains("PawnIsAlive", method);
        Assert.Contains("!IsWarmup()", method);
        Assert.Contains("IsKnife()", method);
        Assert.Contains("WarmupPeriod != true", method);
    }
}
