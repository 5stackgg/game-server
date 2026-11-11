using FiveStack;
using FiveStack.Entities;
using Xunit;

public class AvatarSystemTests
{
    private static MatchMember Member(string? steamId, string? avatarUrl) =>
        new MatchMember { steam_id = steamId, avatar_url = avatarUrl };

    [Fact]
    public void WantsEveryPlayerWithAPhotoOnBothLineups()
    {
        MatchData match = new MatchData
        {
            lineup_1 = new MatchLineUp
            {
                lineup_players =
                [
                    Member("76561198000000001", "/avatars/roster-teams/a.png?format=game"),
                    Member("76561198000000002", null),
                ],
            },
            lineup_2 = new MatchLineUp
            {
                lineup_players = [Member("76561198000000003", "/avatars/roster-teams/b.png?format=game")],
            },
        };

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["76561198000000001"] = "/avatars/roster-teams/a.png?format=game",
                ["76561198000000003"] = "/avatars/roster-teams/b.png?format=game",
            },
            AvatarSystem.Wanted(match)
        );
    }

    // The key is a SteamID64 and the path is fetched from the API, so anything
    // else would write a junk row or fetch from somewhere other than the API.
    [Theory]
    [InlineData(null, "/avatars/roster-teams/a.png?format=game")]
    [InlineData("", "/avatars/roster-teams/a.png?format=game")]
    [InlineData("not-a-steamid", "/avatars/roster-teams/a.png?format=game")]
    [InlineData("76561198000000001", "")]
    [InlineData("76561198000000001", "https://elsewhere.example/a.png")]
    public void SkipsAPlayerWithoutASteamIdOrApiPath(string? steamId, string? avatarUrl)
    {
        MatchData match = new MatchData
        {
            lineup_1 = new MatchLineUp { lineup_players = [Member(steamId, avatarUrl)] },
            lineup_2 = new MatchLineUp(),
        };

        Assert.Empty(AvatarSystem.Wanted(match));
    }

    private static byte[] Png(int length)
    {
        byte[] image = new byte[length];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(image, 0);
        return image;
    }

    [Fact]
    public void TakesAPngUpTo16KiB()
    {
        Assert.True(AvatarSystem.IsUsablePng(Png(5000)));
        Assert.True(AvatarSystem.IsUsablePng(Png(AvatarSystem.MaxBytes)));
    }

    [Fact]
    public void RefusesAnythingElse()
    {
        Assert.False(AvatarSystem.IsUsablePng(Png(AvatarSystem.MaxBytes + 1)));
        Assert.False(AvatarSystem.IsUsablePng(Png(8)));
        Assert.False(AvatarSystem.IsUsablePng(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45 }));
        Assert.False(AvatarSystem.IsUsablePng(Array.Empty<byte>()));
    }
}
