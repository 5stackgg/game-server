using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FiveStack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// Transient services are built fresh for each match's MatchManager. Anything
// that outlives a match and takes one gets a second copy whose state the
// match never sees, so it has to reach the match's own through MatchManager.
public class MatchSystemsWiringTests
{
    private static string SourceDir([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "src"));
    }

    private static string Code(params string[] path)
    {
        return Regex.Replace(
            File.ReadAllText(Path.Combine(new[] { SourceDir() }.Concat(path).ToArray())),
            @"//[^\n]*",
            ""
        );
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
    public void NothingThatOutlivesAMatchIsHandedItsOwnCopyOfAPerMatchSystem()
    {
        ServiceCollection services = new();
        new FiveStackServiceCollection().ConfigureServices(services);

        HashSet<Type> perMatch = services
            .Where(service => service.Lifetime == ServiceLifetime.Transient)
            .Select(service => service.ServiceType)
            .ToHashSet();

        Assert.Contains(typeof(TimeoutSystem), perMatch);
        Assert.Contains(typeof(ReadySystem), perMatch);

        List<string> copies = services
            .Where(service => service.Lifetime == ServiceLifetime.Singleton)
            .Select(service => service.ImplementationType ?? service.ServiceType)
            .Append(typeof(FiveStackPlugin))
            .SelectMany(type =>
                type.GetConstructors()
                    .SelectMany(constructor => constructor.GetParameters())
                    .Where(parameter => perMatch.Contains(parameter.ParameterType))
                    .Select(parameter => $"{type.Name} takes its own {parameter.ParameterType.Name}")
            )
            .ToList();

        Assert.Empty(copies);
    }

    [Fact]
    public void APlayerJoiningATeamIsToldToReadyWhileTheMatchWaitsForReady()
    {
        string join = Method(
            Code("FiveStack.Events", "PlayerConnected.cs"),
            "public HookResult OnPlayerJoinTeam("
        );

        Assert.Contains("if (match.readySystem.IsWaitingForReady())", join);
    }
}
