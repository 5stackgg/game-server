using FiveStack.Utilities;
using Xunit;

public class HealthBookTests
{
    [Fact]
    public void ALethalHitIsCappedAtWhatTheLastHitLeft()
    {
        HealthBook health = new();

        Assert.Equal(70, health.Hit(1, 70, 30));
        Assert.Equal(30, health.Hit(1, 448, 0));
    }

    // Deathmatch heals a player on a kill and fires nothing for it.
    [Fact]
    public void AHealWithoutAnEventIsReadOffThePawn()
    {
        HealthBook health = new();
        health.Hit(1, 70, 30);

        health.Seen(1, 100);

        Assert.Equal(100, health.Hit(1, 448, 0));
    }

    [Fact]
    public void ADeadPawnKeepsWhatItsLastHitLeft()
    {
        HealthBook health = new();
        health.Hit(1, 60, 40);

        health.Seen(1, 0);

        Assert.Equal(40, health.Hit(1, 448, 0));
    }

    [Fact]
    public void ASpawnStartsAtFullHealth()
    {
        HealthBook health = new();
        health.Hit(1, 60, 40);

        health.Spawned(1);

        Assert.Equal(PlayerRoster.FullHealth, health.Hit(1, 448, 0));
    }

    [Fact]
    public void EachSlotKeepsItsOwnHealth()
    {
        HealthBook health = new();
        health.Hit(1, 60, 40);
        health.Seen(2, 90);

        Assert.Equal(40, health.Hit(1, 448, 0));
        Assert.Equal(90, health.Hit(2, 448, 0));
        Assert.Equal(PlayerRoster.FullHealth, health.Hit(3, 448, 0));
    }
}
