using System.Text.RegularExpressions;
using FiveStack.Utilities;
using Xunit;

public class HudSlotResetTests
{
    private const uint Index = 412;

    // Comments out first: a call that has been commented out is not a call.
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

    private static string Hook(string start, string end)
    {
        string source = Code("UtilityPracticePlugin.cs");
        int from = source.IndexOf(start, StringComparison.Ordinal);

        Assert.True(from >= 0, $"{start} is gone");

        int to = source.IndexOf(end, from, StringComparison.Ordinal);

        Assert.True(to >= 0, $"{end} is gone");

        return source[from..to];
    }

    [Fact]
    public void TheSameEntityKeepsItsSurface()
    {
        object layout = new();

        Assert.True(HudLayoutIdentity.Same(layout, Index, layout, Index));
    }

    [Fact]
    public void ALayoutRespawnedOnTheFreedIndexIsANewLayout()
    {
        Assert.False(HudLayoutIdentity.Same(new object(), Index, new object(), Index));
    }

    [Fact]
    public void ALayoutOnAnotherIndexIsANewLayout()
    {
        object layout = new();

        Assert.False(HudLayoutIdentity.Same(layout, Index, layout, Index + 1));
    }

    [Fact]
    public void ADisconnectForgetsTheSlotWithoutResolvingTheLeaver()
    {
        string beforeResolving = Hook("_disconnectHandler = @event =>", "ForPlayer(");

        Assert.Contains("_hud?.Forget(@event.PlayerId);", beforeResolving);
        Assert.Contains(
            "Core.Event.OnClientDisconnected += _disconnectHandler;",
            Code("UtilityPracticePlugin.cs")
        );
    }

    [Fact]
    public void AnArrivalForgetsWhateverTheSlotWasHolding()
    {
        string hook = Hook("_putInServerHandler = @event =>", "};");

        Assert.Contains("_hud?.Forget(@event.PlayerId);", hook);
        Assert.Contains(
            "Core.Event.OnClientPutInServer += _putInServerHandler;",
            Code("UtilityPracticePlugin.cs")
        );
    }

    [Fact]
    public void TheArrivalHookIsLetGoOnUnload()
    {
        Assert.Contains(
            "Core.Event.OnClientPutInServer -= _putInServerHandler;",
            Code("UtilityPracticePlugin.cs")
        );
    }

    [Fact]
    public void AnArrivalLeavesAPlayersOwnHudSettingsAlone()
    {
        Assert.DoesNotContain("ForgetHud", Hook("_putInServerHandler = @event =>", "};"));
    }

    [Fact]
    public void TheKitAsksTheIdentityRuleBeforeKeepingASurface()
    {
        string kit = Code("Hud", "HudKit.cs");

        Assert.Contains(
            "HudLayoutIdentity.Same(open.Surface.Layout, open.EntityIndex, entity, entity.Index)",
            kit
        );
        Assert.DoesNotContain("open.EntityIndex == entity.Index", kit);
    }
}
