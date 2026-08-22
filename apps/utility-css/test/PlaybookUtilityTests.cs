using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Xunit;

// An execute is a clock, and the two ways it goes wrong are silent: a step that
// fires twice, and a step that never fires at all.
public class PlaybookUtilityTests
{
    private static UtilityPlaybookStep Step(
        int order,
        int offsetMs,
        string? assigned = null,
        bool withLineup = true,
        bool seeded = false,
        string? confidence = null
    )
    {
        return new UtilityPlaybookStep
        {
            utility_lineup_id = $"lineup-{order}",
            step_order = order,
            offset_ms = offsetMs,
            assigned_steam_id = assigned,
            note = $"step {order}",
            lineup = withLineup
                ? new UtilityLibraryRow
                {
                    id = $"row-{order}",
                    name = $"utility {order}",
                    utility_type = "Smoke",
                    origin_x = 1f,
                    origin_y = 2f,
                    origin_z = 3f,
                    land_x = 4f,
                    land_y = 5f,
                    land_z = 6f,
                    initial_pos_x = seeded ? 11f : null,
                    initial_pos_y = seeded ? 22f : null,
                    initial_pos_z = seeded ? 33f : null,
                    initial_vel_x = seeded ? 400f : null,
                    initial_vel_y = seeded ? -500f : null,
                    initial_vel_z = seeded ? 600f : null,
                    confidence = confidence,
                }
                : null,
        };
    }

    private static UtilityPlaybook Playbook(params UtilityPlaybookStep[] steps)
    {
        return new UtilityPlaybook
        {
            id = "book",
            name = "A execute",
            map_name = "de_mirage",
            side = "TERRORIST",
            steps = steps.ToList(),
        };
    }

    [Fact]
    public void NoPlaybookIsNoSteps()
    {
        Assert.Empty(PlaybookUtility.Ordered(null));
    }

    [Fact]
    public void StepsAreOrderedByStepOrder()
    {
        var ordered = PlaybookUtility.Ordered(
            Playbook(Step(3, 0), Step(1, 900), Step(2, 400))
        );

        Assert.Equal(new[] { 1, 2, 3 }, ordered.Select(step => step.step_order));
    }

    // A step whose lineup the panel declined to inline cannot be loaded, and
    // teleporting somebody onto nothing is worse than skipping it.
    [Fact]
    public void AStepWithNoLineupIsDropped()
    {
        var ordered = PlaybookUtility.Ordered(
            Playbook(Step(1, 0), Step(2, 100, withLineup: false))
        );

        Assert.Single(ordered);
        Assert.Equal(1, ordered[0].step_order);
    }

    [Fact]
    public void ABookLongerThanItsOwnCapIsTruncated()
    {
        var steps = Enumerable
            .Range(0, PlaybookUtility.MaxSteps + 10)
            .Select(index => Step(index, index * 100))
            .ToArray();

        Assert.Equal(PlaybookUtility.MaxSteps, PlaybookUtility.Ordered(Playbook(steps)).Count);
    }

    [Fact]
    public void AStepAtZeroFiresExactlyOnce()
    {
        var steps = PlaybookUtility.Ordered(Playbook(Step(1, 0), Step(2, 500)));

        Assert.Single(PlaybookUtility.Due(steps, -1, 0));
        Assert.Empty(PlaybookUtility.Due(steps, 0, 0));
        Assert.Empty(PlaybookUtility.Due(steps, 0, 100));
    }

    [Fact]
    public void AWindowClaimsEveryStepInsideIt()
    {
        var steps = PlaybookUtility.Ordered(
            Playbook(Step(1, 0), Step(2, 100), Step(3, 200), Step(4, 5000))
        );

        var due = PlaybookUtility.Due(steps, -1, 250);

        Assert.Equal(new[] { 1, 2, 3 }, due.Select(step => step.step_order));
    }

    [Fact]
    public void WalkingTheWindowFiresEveryStepOnce()
    {
        var steps = PlaybookUtility.Ordered(
            Playbook(Step(1, 0), Step(2, 100), Step(3, 100), Step(4, 2500))
        );

        var fired = new List<int>();

        for (int elapsed = 0; elapsed <= 3000; elapsed += 100)
        {
            fired.AddRange(
                PlaybookUtility.Due(steps, elapsed - 100, elapsed).Select(step => step.step_order)
            );
        }

        Assert.Equal(new[] { 1, 2, 3, 4 }, fired);
    }

    [Fact]
    public void TheDurationIsTheLastOffset()
    {
        Assert.Equal(0, PlaybookUtility.DurationMs(new List<UtilityPlaybookStep>()));
        Assert.Equal(
            2500,
            PlaybookUtility.DurationMs(
                PlaybookUtility.Ordered(Playbook(Step(1, 0), Step(2, 2500)))
            )
        );
    }

    [Fact]
    public void AnUnassignedStepBelongsToEveryone()
    {
        UtilityPlaybookStep step = Step(1, 0);

        Assert.False(PlaybookUtility.IsAssigned(step));
        Assert.True(PlaybookUtility.IsFor(step, 76561198000000001));
        Assert.True(PlaybookUtility.IsFor(step, 76561198000000002));
    }

    [Fact]
    public void AnAssignedStepBelongsToOnlyThatPlayer()
    {
        UtilityPlaybookStep step = Step(1, 0, assigned: " 76561198000000001 ");

        Assert.True(PlaybookUtility.IsAssigned(step));
        Assert.True(PlaybookUtility.IsFor(step, 76561198000000001));
        Assert.False(PlaybookUtility.IsFor(step, 76561198000000002));
    }

    // The step names the lineup; the inlined row is only its geometry. Scoring
    // posts the step's id, so the two must not be allowed to disagree.
    [Fact]
    public void AStepsLineupCarriesTheStepsLineupId()
    {
        LineupRecord? lineup = Step(1, 0).ToLineup();

        Assert.NotNull(lineup);
        Assert.Equal("lineup-1", lineup!.id);
        Assert.Equal("lineup-1", lineup.client_id);
        Assert.Equal("Smoke", lineup.utility_type);
        Assert.Equal(4f, lineup.detonation_position.x);
    }

    [Fact]
    public void AStepWithNoLineupConvertsToNothing()
    {
        Assert.Null(Step(1, 0, withLineup: false).ToLineup());
    }

    // A step inlines the same library row, so an execute re-emits its throws
    // exactly wherever the panel has a seed for them.
    [Fact]
    public void AStepInheritsTheSeedOfTheLineupItNames()
    {
        LineupRecord? seeded = Step(1, 0, seeded: true).ToLineup();

        Assert.NotNull(seeded);
        Assert.Equal(11f, seeded!.initial_position.x);
        Assert.Equal(400f, seeded.initial_velocity.x);
        Assert.True(seeded.initial_velocity.Length() > 0f);
    }

    // An execute re-emits a step exactly only where the panel vouched for it;
    // a mined step is something to practise toward, not to replay.
    [Fact]
    public void AStepIsReplayedOnlyWhenThePanelCalledItExact()
    {
        LineupRecord? exact = Step(1, 0, seeded: true, confidence: "exact").ToLineup();
        LineupRecord? mined = Step(2, 0, seeded: true, confidence: "derived").ToLineup();
        LineupRecord? unknown = Step(3, 0, seeded: true).ToLineup();

        Assert.True(exact!.IsExactlyReplayable());
        Assert.False(mined!.IsExactlyReplayable());
        Assert.False(unknown!.IsExactlyReplayable());

        Assert.True(mined.IsKnownInexact());
        Assert.False(unknown.IsKnownInexact());
    }

    [Fact]
    public void AStepNamingASeedlessLineupIsNotReplayable()
    {
        LineupRecord? plain = Step(1, 0).ToLineup();

        Assert.NotNull(plain);
        Assert.Equal(0f, plain!.initial_velocity.Length());
        Assert.Equal(0f, plain.initial_position.Length());
    }
}
