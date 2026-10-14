using FiveStack.Entities.Practice;
using FiveStack.Enums;
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

public class TechniqueLabelTests
{
    [Theory]
    [InlineData("Stationary", "STAND STILL")]
    [InlineData("Walking", "WALK AND THROW")]
    [InlineData("Running", "RUN AND THROW")]
    [InlineData("Crouch", "CROUCH THROW")]
    [InlineData("Jump", "JUMP THROW")]
    [InlineData("RunJump", "RUN + JUMP THROW")]
    [InlineData("WalkJump", "WALK + JUMP THROW")]
    [InlineData("CrouchJump", "CROUCH + JUMP THROW")]
    public void EveryTechniqueHasItsOwnInstruction(string technique, string expected)
    {
        Assert.Equal(expected, PracticeLineupUtility.TechniqueLabel(technique));
    }

    [Fact]
    public void NoTechniqueIsSilentlyTreatedAsStandingStill()
    {
        // The bug this guards: the old switch matched "Run"/"Walk" while the
        // enum says Running/Walking, so a running throw was taught as a
        // standing one and simply never landed.
        foreach (string name in Enum.GetNames<eThrowTechnique>())
        {
            string label = PracticeLineupUtility.TechniqueLabel(name);

            if (name != nameof(eThrowTechnique.Stationary))
            {
                Assert.NotEqual("STAND STILL", label);
            }
        }
    }

    [Theory]
    [InlineData("Full", "LEFT CLICK")]
    [InlineData("Half", "LEFT + RIGHT CLICK")]
    [InlineData("Drop", "RIGHT CLICK")]
    public void EveryStrengthHasItsOwnClick(string strength, string expected)
    {
        Assert.Equal(expected, PracticeLineupUtility.StrengthLabel(strength));
    }
}

public class TrackedHtmlTests
{
    [Fact]
    public void WordGapsSurviveMarkupCollapsing()
    {
        string html = PracticeLineupUtility.TrackedHtml("stand in");

        // Every gap has to be non-breaking, or HTML folds the three spaces
        // between two words down to one and the words run together.
        Assert.DoesNotContain(" ", html);
        Assert.Equal("S&nbsp;T&nbsp;A&nbsp;N&nbsp;D&nbsp;&nbsp;&nbsp;I&nbsp;N", html);
    }

    [Fact]
    public void PlainTrackingIsUntouched()
    {
        Assert.Equal("S T A N D", PracticeLineupUtility.Tracked("stand"));
    }
}

public class TitleCaseTests
{
    [Theory]
    [InlineData("new window", "New Window")]
    [InlineData("CONNECTOR", "Connector")]
    [InlineData("SMOKE - JUMP THROW - LEFT CLICK", "Smoke - Jump Throw - Left Click")]
    [InlineData("a", "A")]
    public void WordsAreCapitalisedAndTheRestLowered(string input, string expected)
    {
        Assert.Equal(expected, PracticeLineupUtility.TitleCase(input));
    }

    [Fact]
    public void HyphensAreNotWordBreaks()
    {
        // "Write-Up" reads worse than "Write-up", so only whitespace splits.
        Assert.Equal("Write-up On The Web", PracticeLineupUtility.TitleCase("WRITE-UP ON THE WEB"));
    }

    [Fact]
    public void EmptyInputStaysEmpty()
    {
        Assert.Equal("", PracticeLineupUtility.TitleCase(null));
        Assert.Equal("", PracticeLineupUtility.TitleCase("   "));
    }

    [Fact]
    public void RunsOfSpacesDoNotCrash()
    {
        Assert.Equal("Two  Gaps", PracticeLineupUtility.TitleCase("two  gaps"));
    }

    // The panel rejects a practice-result whose lineup id is not a uuid, so
    // this is what decides whether a throw is worth reporting at all. Getting
    // it wrong in either direction is a real failure: too strict and saved
    // lineups stop being scored, too loose and every scratch throw goes back to
    // telling the player the panel did not answer.
    [Theory]
    [InlineData("2ba3c04c-715d-4c4e-bce8-860b97ca6fc3")]
    [InlineData("2BA3C04C-715D-4C4E-BCE8-860B97CA6FC3")]
    public void PanelIdsAreUuids(string id)
    {
        Assert.True(PracticeLineupUtility.IsPanelId(id));
    }

    // "2171u, needs 96u" is two numbers in a unit nobody thinks in. The line
    // exists to say whether a throw was close or nowhere near, and units answer
    // neither.
    [Fact]
    public void DistancesAreSaidInMetres()
    {
        Assert.Equal("41m", PracticeLineupUtility.Metres(2171f));
        Assert.Equal("1.8m", PracticeLineupUtility.Metres(96f));
    }

    // Under ten metres the decimal is the whole point: half a metre off is a
    // good smoke and three metres off is not, and both round to the same whole
    // number.
    [Fact]
    public void ShortDistancesKeepTheirDecimal()
    {
        Assert.Contains(".", PracticeLineupUtility.Metres(30f));
        Assert.DoesNotContain(".", PracticeLineupUtility.Metres(2171f));
    }

    [Fact]
    public void ADistanceIsNeverNegative()
    {
        Assert.Equal(PracticeLineupUtility.Metres(96f), PracticeLineupUtility.Metres(-96f));
    }

    [Theory]
    [InlineData("scratch-draft")]
    [InlineData("scratch-de_mirage:Smoke:-29,14:-8,5")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ScratchIdsAreNotPanelIds(string? id)
    {
        Assert.False(PracticeLineupUtility.IsPanelId(id));
    }

    private static LineupRecord Aimed(eThrowTechnique technique, float feet, float eye)
    {
        return new LineupRecord
        {
            technique = technique.ToString(),
            release = new ThrowSnapshot
            {
                feet_position = new Vec3(0f, 0f, feet),
                eye_position = new Vec3(0f, 0f, eye),
            },
        };
    }

    // A throw made standing still is aimed from the eye it recorded, which is
    // what tells a crouched lineup from a standing one.
    [Theory]
    [InlineData(eThrowTechnique.Stationary, 64.06f, 64.06f)]
    [InlineData(eThrowTechnique.Stationary, 46.04f, 46.04f)]
    [InlineData(eThrowTechnique.Crouch, 46.04f, 46.04f)]
    public void AStandingThrowIsAimedFromTheEyeItRecorded(
        eThrowTechnique technique,
        float recorded,
        float expected
    )
    {
        Assert.Equal(
            expected,
            PracticeLineupUtility.AimEyeHeight(Aimed(technique, 100f, 100f + recorded)),
            2
        );
    }

    // A jump throw is released in the air and a run throw somewhere else, so
    // neither recorded the eye it was lined up from; and a row with no eye at
    // all recorded nothing.
    [Theory]
    [InlineData(eThrowTechnique.Jump, 100f, 191f, PracticeLineupUtility.StandingEyeHeight)]
    [InlineData(eThrowTechnique.RunJump, 100f, 191f, PracticeLineupUtility.StandingEyeHeight)]
    [InlineData(eThrowTechnique.Running, 100f, 164f, PracticeLineupUtility.StandingEyeHeight)]
    [InlineData(eThrowTechnique.CrouchJump, 100f, 180f, PracticeLineupUtility.CrouchedEyeHeight)]
    [InlineData(eThrowTechnique.Crouch, 100f, 0f, PracticeLineupUtility.CrouchedEyeHeight)]
    [InlineData(eThrowTechnique.Stationary, 100f, 0f, PracticeLineupUtility.StandingEyeHeight)]
    public void AnyOtherThrowIsAimedFromItsStance(
        eThrowTechnique technique,
        float feet,
        float eye,
        float expected
    )
    {
        Assert.Equal(expected, PracticeLineupUtility.AimEyeHeight(Aimed(technique, feet, eye)));
    }

    // The game stops a look down a degree short of straight down, so the
    // crosshair lands in front of the feet. The dot was drawn on the feet,
    // where no crosshair could ever be put.
    [Theory]
    [InlineData(eThrowTechnique.Stationary, 164f, 0f, 1.117f, 0f)]
    [InlineData(eThrowTechnique.Crouch, 146f, 90f, 0f, 0.803f)]
    public void TheLookDownDotIsWhereTheCrosshairLandsFacingTheThrow(
        eThrowTechnique technique,
        float eye,
        float yaw,
        float x,
        float y
    )
    {
        LineupRecord lineup = Aimed(technique, 100f, eye);
        lineup.release.yaw = yaw;

        Vec3 dot = PracticeLineupUtility.LookDownPoint(lineup, new Vec3(10f, 20f, 100f));

        Assert.Equal(10f + x, dot.x, 2);
        Assert.Equal(20f + y, dot.y, 2);
        Assert.Equal(100f, dot.z, 2);
    }

    [Fact]
    public void AnglesToAPointAreCs2ViewAngles()
    {
        (float yaw, float pitch) = PracticeLineupUtility.AnglesTo(new Vec3(0f, 0f, 0f), new Vec3(0f, 100f, 100f));

        Assert.Equal(90f, yaw, 3);
        Assert.Equal(-45f, pitch, 3);
    }

    // The marker is drawn where the recorded angles meet a wall from the
    // recorded eye. From an eye a few units lower, the recorded angles point
    // below the marker: "lined up" against them said yes with the crosshair
    // under the marker and no with it on. On the marker is the answer.
    [Fact]
    public void FromALowerEyeTheRecordedAnglesMissTheMarker()
    {
        var recordedEye = new Vec3(0f, 0f, 64f);
        var marker = new Vec3(100f, 0f, 64f + 100f * MathF.Tan(10f * MathF.PI / 180f));
        var lowerEye = new Vec3(0f, 0f, 61f);

        (float yaw, float pitch) = PracticeLineupUtility.AnglesTo(lowerEye, marker);
        float againstRecorded = PracticeLineupUtility.AimError(yaw, pitch, 0f, -10f);

        Assert.True(againstRecorded > 1.5f);
        Assert.Equal(-10f, PracticeLineupUtility.AnglesTo(recordedEye, marker).pitch, 2);
    }
}
