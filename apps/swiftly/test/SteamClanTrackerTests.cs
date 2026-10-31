using FiveStack.Utilities;
using Xunit;

// The plugin tells the api which Steam group a player wears as their clan tag,
// once per change, and a slot's next player never inherits the last one's.
public class SteamClanTrackerTests
{
    [Fact]
    public void A_New_Clan_Is_A_Change()
    {
        SteamClanTracker tracker = new();

        Assert.True(tracker.Record(1, 46317375));
        Assert.Equal(46317375u, tracker.Get(1));
    }

    [Fact]
    public void The_Game_Reapplying_The_Same_Clan_Is_Not_A_Change()
    {
        SteamClanTracker tracker = new();
        tracker.Record(1, 46317375);

        Assert.False(tracker.Record(1, 46317375));
    }

    [Fact]
    public void Picking_Another_Group_Is_A_Change()
    {
        SteamClanTracker tracker = new();
        tracker.Record(1, 46317375);

        Assert.True(tracker.Record(1, 0));
        Assert.Equal(0u, tracker.Get(1));
    }

    [Fact]
    public void A_Forgotten_Slot_Has_No_Clan()
    {
        SteamClanTracker tracker = new();
        tracker.Record(1, 46317375);
        tracker.Record(2, 7);

        tracker.Forget(1);

        Assert.Null(tracker.Get(1));
        Assert.Equal(7u, tracker.Get(2));
        Assert.True(tracker.Record(1, 46317375));
    }
}
