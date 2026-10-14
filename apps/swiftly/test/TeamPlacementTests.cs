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
    [Fact]
    public void ThePluginNeverRespawnsAPlayer()
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

        Assert.Empty(respawns);
    }
}
