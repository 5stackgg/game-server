using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Xunit;

public class PracticeLineupUtilityTests
{
    private static LineupRecord Lineup(string name, float x = 0f, float y = 0f)
    {
        return new LineupRecord
        {
            name = name,
            release = new ThrowSnapshot { feet_position = new Vec3(x, y, 0f) },
        };
    }

    [Fact]
    public void MapsProjectilesToUtilityTypes()
    {
        Assert.Equal("Smoke", PracticeLineupUtility.UtilityTypeForProjectile("smokegrenade_projectile"));
        Assert.Equal("Flash", PracticeLineupUtility.UtilityTypeForProjectile("flashbang_projectile"));
        Assert.Equal("HighExplosive", PracticeLineupUtility.UtilityTypeForProjectile("hegrenade_projectile"));
        Assert.Equal("Decoy", PracticeLineupUtility.UtilityTypeForProjectile("decoy_projectile"));
        Assert.Null(PracticeLineupUtility.UtilityTypeForProjectile("weapon_ak47"));
    }

    // Both entity names produce a Molotov: incendiary and molotov differ to the
    // engine but are one lineup type to a player.
    [Fact]
    public void TreatsIncendiaryAndMolotovAsOneType()
    {
        Assert.Equal("Molotov", PracticeLineupUtility.UtilityTypeForProjectile("molotov_projectile"));
        Assert.Equal("Molotov", PracticeLineupUtility.UtilityTypeForProjectile("incendiarygrenade_projectile"));
    }

    [Fact]
    public void MapsUtilityTypesBackToWeapons()
    {
        Assert.Equal("weapon_smokegrenade", PracticeLineupUtility.WeaponForUtilityType("Smoke"));
        Assert.Null(PracticeLineupUtility.WeaponForUtilityType("NotAThing"));
    }

    [Fact]
    public void RecognisesGrenadesInHand()
    {
        Assert.True(PracticeLineupUtility.IsGrenadeWeapon("weapon_smokegrenade"));
        Assert.True(PracticeLineupUtility.IsGrenadeWeapon("weapon_incgrenade"));
        Assert.False(PracticeLineupUtility.IsGrenadeWeapon("weapon_ak47"));
    }

    // Typing a full name must win outright, even when another lineup is closer.
    [Fact]
    public void ExactNameBeatsProximity()
    {
        var lineups = new[] { Lineup("Window", 1000f), Lineup("Window Long", 0f) };
        var resolved = PracticeLineupUtility.Resolve(lineups, "Window", new Vec3(0f, 0f, 0f));
        Assert.Equal("Window", resolved?.name);
    }

    [Fact]
    public void UniquePrefixResolves()
    {
        var lineups = new[] { Lineup("Window"), Lineup("Jungle") };
        Assert.Equal("Jungle", PracticeLineupUtility.Resolve(lineups, "Jun")?.name);
    }

    [Fact]
    public void AmbiguousPrefixFallsBackToNearest()
    {
        var lineups = new[] { Lineup("Window A", 900f), Lineup("Window B", 10f) };
        var resolved = PracticeLineupUtility.Resolve(lineups, "Window", new Vec3(0f, 0f, 0f));
        Assert.Equal("Window B", resolved?.name);
    }

    [Fact]
    public void EmptyQueryPicksTheNearest()
    {
        var lineups = new[] { Lineup("Far", 900f), Lineup("Near", 5f) };
        var resolved = PracticeLineupUtility.Resolve(lineups, "", new Vec3(0f, 0f, 0f));
        Assert.Equal("Near", resolved?.name);
    }

    [Fact]
    public void NoMatchResolvesToNothing()
    {
        var lineups = new[] { Lineup("Window") };
        Assert.Null(PracticeLineupUtility.Resolve(lineups, "Ramp"));
        Assert.Null(PracticeLineupUtility.Resolve(Array.Empty<LineupRecord>(), "Window"));
    }

    [Fact]
    public void FilterWithoutAQueryKeepsEverything()
    {
        var lineups = new[] { Lineup("Window"), Lineup("Jungle") };
        Assert.Equal(2, PracticeLineupUtility.Filter(lineups, "").Count);
    }

    [Fact]
    public void FilterMatchesAnywhereInTheNameAndIgnoresCase()
    {
        var lineups = new[] { Lineup("Window Long"), Lineup("Deep Jungle"), Lineup("Ramp") };
        var matches = PracticeLineupUtility.Filter(lineups, "un");

        Assert.Single(matches);
        Assert.Equal("Deep Jungle", matches[0].name);
    }

    // .next and .prev walk this list, so the order is the one the player would
    // expect: whatever is closest first.
    [Fact]
    public void FilterOrdersByDistanceWhenGivenAPosition()
    {
        var lineups = new[] { Lineup("Window Far", 900f), Lineup("Window Near", 5f) };
        var matches = PracticeLineupUtility.Filter(lineups, "Window", new Vec3(0f, 0f, 0f));

        Assert.Equal("Window Near", matches[0].name);
        Assert.Equal("Window Far", matches[1].name);
    }

    [Fact]
    public void FilterReturnsNothingWhenTheQueryMatchesNothing()
    {
        var lineups = new[] { Lineup("Window") };
        Assert.Empty(PracticeLineupUtility.Filter(lineups, "Ramp"));
    }

    [Fact]
    public void NormalizingIsCaseInsensitive()
    {
        Assert.Equal("HighExplosive", PracticeLineupUtility.NormalizeUtilityType("he"));
        Assert.Equal("Flash", PracticeLineupUtility.NormalizeUtilityType("FLASHBANG"));
    }

    // An unknown value is passed through rather than guessed at: the API will
    // reject it loudly, which beats storing it as the wrong type.
    [Fact]
    public void NormalizingLeavesAnUnknownTypeAlone()
    {
        Assert.Equal("Banana", PracticeLineupUtility.NormalizeUtilityType("Banana"));
    }
}
