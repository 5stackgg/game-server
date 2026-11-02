using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// RoundRestorePublishTests hands MatchEvents a state of its own, so it cannot
// see whether the plugin wires the same one in or calls it from the right place.
// That code needs the engine, so these read it.
public class RoundRestoreWiringTests
{
    private static readonly string[] BuildOutput = ["bin", "obj", "build"];

    private static string SourceDir([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "src"));
    }

    private static string Strip(string source)
    {
        return Regex.Replace(source, @"//[^\n]*", "");
    }

    private static string Service(string file)
    {
        return Strip(File.ReadAllText(Path.Combine(SourceDir(), "FiveStack.Services", file)));
    }

    private static string Method(string source, string signature)
    {
        int from = source.IndexOf(signature, StringComparison.Ordinal);

        Assert.True(from >= 0, $"{signature} is gone");

        int to = source.IndexOf("\n    }\n", from, StringComparison.Ordinal);

        Assert.True(to > from, $"the end of {signature} was not found");

        return source[from..to];
    }

    [Fact]
    public void ThePluginHasOneRestoreState()
    {
        IEnumerable<string> sources = Directory
            .EnumerateFiles(SourceDir(), "*.cs", SearchOption.AllDirectories)
            .Where(file =>
                !Path.GetRelativePath(SourceDir(), file)
                    .Split(Path.DirectorySeparatorChar)
                    .Any(BuildOutput.Contains)
            )
            .Select(file => Strip(File.ReadAllText(file)));

        Assert.Single(sources, source => source.Contains("AddSingleton<RoundRestoreState>()"));
    }

    [Theory]
    [InlineData("MatchEvents.cs", "public MatchEvents(")]
    [InlineData("GameBackUpRounds.cs", "public GameBackUpRounds(")]
    public void BothSidesAreHandedThatState(string file, string constructor)
    {
        Assert.Contains("RoundRestoreState restore", Method(Service(file), constructor));
    }

    [Fact]
    public void ALoadIsMarkedAsALoad()
    {
        string restore = Method(Service("GameBackUpRounds.cs"), "public void RestoreRound(int round)");

        Assert.Contains("_restore.StartLoad(", restore);
        Assert.DoesNotContain("_restore.StartVote(", restore);
    }

    [Fact]
    public void AVoteIsNotMarkedAsALoad()
    {
        string request = Method(
            Service("GameBackUpRounds.cs"),
            "public void RequestRestoreBackupRound("
        );

        Assert.Contains("_restore.StartVote(", request);
        Assert.DoesNotContain("_restore.StartLoad(", request);
    }

    [Fact]
    public void TheFilterComesBeforeAnythingElseInThePublish()
    {
        string publish = Method(Service("MatchEvents.cs"), "public void PublishGameEvent(");
        int filter = publish.IndexOf(
            "RoundRestoreGateUtility.DroppedDuringLoad(Event, _restore.LoadingBackup)",
            StringComparison.Ordinal
        );
        int offline = publish.IndexOf("IsOfflineMode", StringComparison.Ordinal);

        Assert.True(filter >= 0, "PublishGameEvent does not filter");
        Assert.True(filter < offline, "the filter runs after the first way out");
    }
}
