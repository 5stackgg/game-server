using FiveStack.Utilities;
using Xunit;

public class CommandTriggerTests
{
    private const string StockCore =
        """
        {
            "CS2ServerGuidelines": "https://blog.counter-strike.net/index.php/server_guidelines/",
            "CommandPrefixes": [
                "!"
            ],
            "CommandSilentPrefixes": [
                "/"
            ],
            "AutoHotReload": true,
            "ProfilerLevel": 0,
            "FollowCS2ServerGuidelines": true,
            "Menu": {
                "NavigationPrefix": "➤",
                "ItemsPerPage": 5
            },
            "UsePlayerLanguage": true
        }
        """;

    private const string FiveStackCore =
        """
        {
            "CS2ServerGuidelines": "https://blog.counter-strike.net/index.php/server_guidelines/",
            "CommandPrefixes": [
                ".",
                "!"
            ],
            "CommandSilentPrefixes": [
                "/"
            ],
            "Menu": {
                "InputMode": "button",
                // "KindSettings": {
                //     "Center": {
                //         "ItemsPerPage": 4
                //     }
                // },
                "ItemsPerPage": 5
            }
        }
        """;

    [Fact]
    public void Stock_Core_Config_Hints_The_Prefix_Swiftly_Actually_Answers()
    {
        Assert.Equal("!", CommandUtility.ResolveTrigger(".", StockCore, "CommandPrefixes", "!"));
    }

    [Fact]
    public void FiveStack_Core_Config_With_Comments_Keeps_The_Configured_Trigger()
    {
        Assert.Equal(".", CommandUtility.ResolveTrigger(".", FiveStackCore, "CommandPrefixes", "!"));
        Assert.Equal("/", CommandUtility.ResolveTrigger("/", FiveStackCore, "CommandSilentPrefixes", "/"));
    }

    [Fact]
    public void Configured_Trigger_Wins_When_Core_Lists_It_Anywhere()
    {
        Assert.Equal("!", CommandUtility.ResolveTrigger("!", FiveStackCore, "CommandPrefixes", "!"));
    }

    [Fact]
    public void Missing_Key_Falls_Back_To_The_Swiftly_Default()
    {
        const string noPrefixes = """{ "Language": "en" }""";

        Assert.Equal("!", CommandUtility.ResolveTrigger(".", noPrefixes, "CommandPrefixes", "!"));
        Assert.Equal("/", CommandUtility.ResolveTrigger(".", noPrefixes, "CommandSilentPrefixes", "/"));
    }

    [Fact]
    public void Empty_Prefix_List_Falls_Back_To_The_Swiftly_Default()
    {
        Assert.Equal(
            "!",
            CommandUtility.ResolveTrigger(".", """{ "CommandPrefixes": [] }""", "CommandPrefixes", "!")
        );
    }

    // SwiftlyS2 parses a broken core.jsonc as empty and registers every key at
    // its default, so "!" is what it answers -- not whatever we configured.
    [Fact]
    public void Malformed_Core_Config_Means_The_Swiftly_Default()
    {
        Assert.Equal(
            "!",
            CommandUtility.ResolveTrigger(".", """{ "CommandPrefixes": ["."], """, "CommandPrefixes", "!")
        );
        Assert.Equal(
            "!",
            CommandUtility.ResolveTrigger(".", """{ "CommandPrefixes": ["."], }""", "CommandPrefixes", "!")
        );
    }

    [Fact]
    public void Unreadable_Core_Config_Keeps_The_Configured_Trigger()
    {
        Assert.Equal(".", CommandUtility.ResolveTrigger(".", null, "CommandPrefixes", "!"));
    }
}
