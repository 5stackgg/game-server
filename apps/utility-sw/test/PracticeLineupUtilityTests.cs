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

public class UtilityBySpotTests
{
    private static List<(float, float, float, List<string>)> Group(
        params (float x, float y, float z, string type)[] throws
    )
    {
        return PracticeLineupUtility.UtilityBySpot(throws, 40f, 72f);
    }

    [Fact]
    public void TwoSmokesFromOneSpotAreOneSmoke()
    {
        var spots = Group((100f, 100f, 0f, "Smoke"), (108f, 96f, 0f, "Smoke"));

        Assert.Single(spots);
        Assert.Equal(new[] { "Smoke" }, spots[0].Item4);
    }

    [Fact]
    public void ASpotWithTwoKindsShowsBoth()
    {
        var spots = Group((100f, 100f, 0f, "Smoke"), (110f, 100f, 0f, "Flash"));

        Assert.Single(spots);
        Assert.Equal(new[] { "Smoke", "Flash" }, spots[0].Item4);
    }

    [Fact]
    public void SpotsFurtherApartThanTheRadiusStaySeparate()
    {
        var spots = Group((100f, 100f, 0f, "Smoke"), (200f, 100f, 0f, "Smoke"));

        Assert.Equal(2, spots.Count);
    }

    [Fact]
    public void TheSamePositionOnAnotherFloorIsAnotherSpot()
    {
        var spots = Group((100f, 100f, 0f, "Smoke"), (100f, 100f, 128f, "Smoke"));

        Assert.Equal(2, spots.Count);
    }

    [Fact]
    public void GroupingIsByDistanceNotByAGrid()
    {
        // Two throws either side of a grid line are one spot; a naive round()
        // would split them and draw the model twice.
        var spots = Group((39f, 0f, 0f, "Smoke"), (41f, 0f, 0f, "Smoke"));

        Assert.Single(spots);
    }

    [Fact]
    public void NothingInNothingOut()
    {
        Assert.Empty(PracticeLineupUtility.UtilityBySpot([], 40f, 72f));
    }
}

public class AimMissTests
{
    [Fact]
    public void InsideToleranceIsFullyOn()
    {
        Assert.Equal(0f, PracticeLineupUtility.AimMiss(0.2f, 0.35f));
        Assert.Equal(0f, PracticeLineupUtility.AimMiss(0.35f, 0.35f));
    }

    [Fact]
    public void JustOutsideToleranceIsNotYetRed()
    {
        float miss = PracticeLineupUtility.AimMiss(0.4f, 0.35f);

        Assert.True(miss > 0f);
        Assert.True(miss < 0.1f);
    }

    [Fact]
    public void FarOffIsFullyRed()
    {
        Assert.Equal(1f, PracticeLineupUtility.AimMiss(90f, 0.35f));
    }

    [Fact]
    public void AWiderToleranceStaysGreenLonger()
    {
        Assert.Equal(0f, PracticeLineupUtility.AimMiss(1.5f, 2f));
        Assert.True(PracticeLineupUtility.AimMiss(1.5f, 0.35f) > 0f);
    }

    [Fact]
    public void ALineupThatNeverSaidFallsBackToTheDefault()
    {
        Assert.Equal(
            PracticeLineupUtility.AimMiss(0.5f, PracticeLineupUtility.DefaultAimTolerance),
            PracticeLineupUtility.AimMiss(0.5f, 0f)
        );
    }

    [Fact]
    public void ErrorIsTheWorseOfTheTwoAxes()
    {
        Assert.Equal(3f, PracticeLineupUtility.AimError(0f, 3f, 0f, 0f));
        Assert.Equal(3f, PracticeLineupUtility.AimError(3f, 0f, 0f, 0f));
    }

    [Fact]
    public void ErrorTakesTheShortWayRoundTheCircle()
    {
        // 359 and 1 are two degrees apart, not 358.
        Assert.Equal(2f, PracticeLineupUtility.AimError(359f, 0f, 1f, 0f));
    }

    [Fact]
    public void MissNeverLeavesTheZeroToOneRange()
    {
        foreach (float error in new[] { 0f, 0.01f, 1f, 5f, 50f, 179f })
        {
            float miss = PracticeLineupUtility.AimMiss(error, 0.35f);

            Assert.InRange(miss, 0f, 1f);
        }
    }
}

public class StanceMissTests
{
    [Fact]
    public void StandingOnTheSpotIsFullyOn()
    {
        Assert.Equal(0f, PracticeLineupUtility.StanceMiss(0f));
        Assert.Equal(0f, PracticeLineupUtility.StanceMiss(8f));
    }

    [Fact]
    public void DriftingOffRampsUp()
    {
        float near = PracticeLineupUtility.StanceMiss(12f);
        float far = PracticeLineupUtility.StanceMiss(30f);

        Assert.True(near > 0f);
        Assert.True(far > near);
        Assert.True(far < 1f);
    }

    [Fact]
    public void WellOffTheSpotIsFullyRed()
    {
        Assert.Equal(1f, PracticeLineupUtility.StanceMiss(48f));
        Assert.Equal(1f, PracticeLineupUtility.StanceMiss(500f));
    }

    [Fact]
    public void StanceToleranceIsTighterThanTheSpotItself()
    {
        // SpotRadius asks "is this the same place"; this asks "are you on it".
        Assert.True(PracticeLineupUtility.StanceToleranceUnits < 40f);
    }
}

public class MissBucketTests
{
    [Fact]
    public void GreenIsReservedForInsideTolerance()
    {
        Assert.Equal(0, PracticeLineupUtility.MissBucket(0f, 5));

        // The smallest possible miss is already NOT green -- this is the whole
        // point: the colour and LINED UP must never disagree.
        Assert.NotEqual(0, PracticeLineupUtility.MissBucket(0.001f, 5));
    }

    [Fact]
    public void OutsideToleranceRampsAcrossTheRemainingSteps()
    {
        Assert.Equal(1, PracticeLineupUtility.MissBucket(0.05f, 5));
        Assert.Equal(4, PracticeLineupUtility.MissBucket(1f, 5));
        Assert.Equal(4, PracticeLineupUtility.MissBucket(0.9f, 5));
    }

    [Fact]
    public void EveryMissLandsInsideTheStepRange()
    {
        foreach (float miss in new[] { 0f, 0.001f, 0.2f, 0.5f, 0.99f, 1f })
        {
            Assert.InRange(PracticeLineupUtility.MissBucket(miss, 5), 0, 4);
        }
    }
}
