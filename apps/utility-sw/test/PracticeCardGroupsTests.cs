using FiveStack.Utilities;
using Xunit;

namespace FiveStack.Tests;

public class PracticeCardGroupsTests
{
    [Fact]
    public void AFlashOnABoxOfBotsIsCountedByWhatItDid()
    {
        var hits = new List<PracticeCardGroups.FlashHit>
        {
            new(4.2f, 1.2f, Target: true, Bad: false),
            new(3.5f, 0.5f, Target: true, Bad: false),
            new(4.8f, 1.8f, Target: true, Bad: false),
            new(2.0f, 0f, Target: true, Bad: false),
            new(3.9f, 0.9f, Target: true, Bad: false),
            new(2.5f, 0f, Target: false, Bad: true),
        };

        IReadOnlyList<PracticeCardGroups.Group> groups = PracticeCardGroups.Flash(hits);

        Assert.Equal(new[] { "blinded", "your side" }, groups.Select(group => group.Label));
        Assert.Equal(new[] { 5, 1 }, groups.Select(group => group.Count));
        Assert.Equal(4.8f, groups[0].Seconds);
        Assert.True(groups[1].Bad);
    }

    [Fact]
    public void ThreeSecondsOfBlindIsOverASecondBlind()
    {
        var hits = new List<PracticeCardGroups.FlashHit>
        {
            new(3.0f, 0f, Target: true, Bad: false),
        };

        Assert.Equal(1, PracticeCardGroups.Worked(hits));
        Assert.Equal(new[] { ("blinded", 1) }, PracticeCardGroups.Flash(hits).Select(group => (group.Label, group.Count)));
    }

    [Fact]
    public void UnderASecondOrOnYourOwnSideIsNotAWorkedFlash()
    {
        var hits = new List<PracticeCardGroups.FlashHit>
        {
            new(0.9f, 0f, Target: true, Bad: false),
            new(4.0f, 1f, Target: false, Bad: true),
        };

        Assert.Equal(0, PracticeCardGroups.Worked(hits));
    }

    [Fact]
    public void ARowThatReadsASecondCountsAsASecond()
    {
        Assert.Equal(1, PracticeCardGroups.Worked(new List<PracticeCardGroups.FlashHit> { new(0.96f, 0f, Target: true, Bad: false) }));
        Assert.Equal(1, PracticeCardGroups.Worked(new List<PracticeCardGroups.FlashHit> { new(1f, 0f, Target: true, Bad: false) }));
        Assert.Equal(0, PracticeCardGroups.Worked(new List<PracticeCardGroups.FlashHit> { new(0.94f, 0f, Target: true, Bad: false) }));
    }

    [Fact]
    public void UnderASecondIsDazzledAndStillOnTheCard()
    {
        var hits = new List<PracticeCardGroups.FlashHit>
        {
            new(3.0f, 0f, Target: true, Bad: false),
            new(0.8f, 0f, Target: true, Bad: false),
            new(0.6f, 0f, Target: true, Bad: false),
        };

        Assert.Equal(new[] { ("blinded", 1), ("dazzled", 2) }, PracticeCardGroups.Flash(hits).Select(group => (group.Label, group.Count)));
    }

    [Fact]
    public void AFireCountsWhoItReachedNotHowMuch()
    {
        var burns = Enumerable
            .Repeat(new PracticeCardGroups.Burn(Target: true, Bad: false), 7)
            .Append(new PracticeCardGroups.Burn(Target: false, Bad: true))
            .ToList();

        IReadOnlyList<PracticeCardGroups.Group> groups = PracticeCardGroups.Fire(burns);

        Assert.Equal(new[] { ("burned", 7), ("your side", 1) }, groups.Select(group => (group.Label, group.Count)));
    }

    [Fact]
    public void NothingCaughtIsNoRows()
    {
        Assert.Empty(PracticeCardGroups.Flash(new List<PracticeCardGroups.FlashHit>()));
        Assert.Empty(PracticeCardGroups.Fire(new List<PracticeCardGroups.Burn>()));
    }
}
