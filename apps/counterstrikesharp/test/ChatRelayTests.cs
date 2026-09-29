using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

public class ChatRelayTests
{
    private const string Gagged = "76561198000000001";
    private const string Free = "76561198000000002";

    private static MatchData BuildMatch()
    {
        return new MatchData
        {
            lineup_1 = new MatchLineUp
            {
                lineup_players = new List<MatchMember>
                {
                    new MatchMember { steam_id = Gagged, name = "Sub", is_gagged = true },
                },
            },
            lineup_2 = new MatchLineUp
            {
                lineup_players = new List<MatchMember>
                {
                    new MatchMember { steam_id = Free, name = "Free" },
                },
            },
        };
    }

    private static string[] Args(string line, params string[] rest)
    {
        return [line, .. rest];
    }

    [Fact]
    public void ParseWebChat_NameStartingWithOrganizerTagIsNotAnOrganizer()
    {
        string[][] cases = [Args("[organizer] Mallory: gg"), Args("[organizer] Mallory: gg", "0")];

        foreach (string[] args in cases)
        {
            (string text, bool organizer) = ChatUtility.ParseWebChat(args);

            Assert.False(organizer);
            Assert.Equal("[organizer] Mallory: gg", text);
        }
    }

    [Fact]
    public void ParseWebChat_TokensSmuggledOutOfTheLineAreNotTheFlag()
    {
        Assert.False(ChatUtility.ParseWebChat(Args("Mallory: gg", "1", "", "0")).Organizer);
        Assert.False(ChatUtility.ParseWebChat(Args("Mallory: gg", "1 0")).Organizer);
    }

    [Theory]
    [InlineData("Alice: gg")]
    [InlineData("[organizer] Alice: gg")]
    public void ParseWebChat_OrganizerComesFromTheFlag(string line)
    {
        (string text, bool organizer) = ChatUtility.ParseWebChat(Args(line, "1"));

        Assert.True(organizer);
        Assert.Equal("Alice: gg", text);
    }

    [Theory]
    [InlineData("Bob: [red]gg", "Bob: gg")]
    [InlineData("Bob: [RED]g[Lime]g", "Bob: gg")]
    [InlineData("[teamcolor]Bob: gg", "Bob: gg")]
    [InlineData("Bob: gg[newline]Console: match cancelled", "Bob: ggConsole: match cancelled")]
    [InlineData("Bob: [re[red]d]gg", "Bob: gg")]
    [InlineData("Bob: [r\u0001ed]gg", "Bob: gg")]
    [InlineData("Bob: \u0007g\u0002g\u0010", "Bob: gg")]
    [InlineData("Bob: [AWP] {red} [organizer] gg", "Bob: [AWP] {red} [organizer] gg")]
    public void ParseWebChat_StripsChatFormatting(string line, string expected)
    {
        Assert.Equal(expected, ChatUtility.ParseWebChat(Args(line)).Text);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("/")]
    [InlineData("white")]
    [InlineData("darkred")]
    [InlineData("lightpurple")]
    [InlineData("green")]
    [InlineData("olive")]
    [InlineData("lime")]
    [InlineData("red")]
    [InlineData("gray")]
    [InlineData("grey")]
    [InlineData("lightyellow")]
    [InlineData("yellow")]
    [InlineData("silver")]
    [InlineData("bluegrey")]
    [InlineData("lightblue")]
    [InlineData("blue")]
    [InlineData("darkblue")]
    [InlineData("purple")]
    [InlineData("magenta")]
    [InlineData("lightred")]
    [InlineData("gold")]
    [InlineData("orange")]
    [InlineData("teamcolor")]
    [InlineData("newline")]
    public void StripFormatting_RemovesEverySwiftlyTag(string tag)
    {
        string upper = tag.ToUpperInvariant();

        Assert.Equal("Bob: gg", ChatUtility.StripFormatting($"Bob: [{tag}]g[{upper}]g"));
    }

    [Fact]
    public void AllChatRoute_GaggedSpectatorIsBlocked()
    {
        Assert.Equal(
            eAllChatRoute.Block,
            MatchUtility.AllChatRoute(BuildMatch(), Gagged, "Sub", spectator: true)
        );
    }

    [Fact]
    public void AllChatRoute_GaggedPlayerIsBlocked()
    {
        Assert.Equal(
            eAllChatRoute.Block,
            MatchUtility.AllChatRoute(BuildMatch(), Gagged, "Sub", spectator: false)
        );
    }

    [Fact]
    public void AllChatRoute_PlaceholderPrefixDoesNotHideAGag()
    {
        MatchData match = BuildMatch();
        match.lineup_1.lineup_players.Insert(
            0,
            new MatchMember { steam_id = null, placeholder_name = "Subway" }
        );

        Assert.Equal(
            eAllChatRoute.Block,
            MatchUtility.AllChatRoute(match, Gagged, "Sub", spectator: false)
        );
    }

    [Fact]
    public void AllChatRoute_SpectatorIsRelayed()
    {
        Assert.Equal(
            eAllChatRoute.Spectator,
            MatchUtility.AllChatRoute(BuildMatch(), Free, "Free", spectator: true)
        );
        Assert.Equal(
            eAllChatRoute.Spectator,
            MatchUtility.AllChatRoute(null, Free, "Free", spectator: true)
        );
    }

    [Fact]
    public void AllChatRoute_PlayerIsPublished()
    {
        Assert.Equal(
            eAllChatRoute.Publish,
            MatchUtility.AllChatRoute(BuildMatch(), Free, "Free", spectator: false)
        );
        Assert.Equal(
            eAllChatRoute.NoMatch,
            MatchUtility.AllChatRoute(null, Free, "Free", spectator: false)
        );
    }
}
