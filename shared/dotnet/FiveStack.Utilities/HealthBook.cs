namespace FiveStack.Utilities;

// Each player's health by slot, so a lethal hit is credited only with what it
// took. A hit says what it left and a spawn clears it, but deathmatch heals a
// player on a kill without an event, so the tick reads it off the pawn too.
// Only touched on the game thread.
public sealed class HealthBook
{
    private readonly Dictionary<int, int> _health = new();

    // Returns the damage the hit dealt, capped by PlayerRoster.Dealt.
    public int Hit(int slot, int damage, int healthAfter)
    {
        int dealt = PlayerRoster.Dealt(
            damage,
            healthAfter,
            _health.TryGetValue(slot, out int before) ? before : null
        );
        _health[slot] = healthAfter;

        return dealt;
    }

    public void Spawned(int slot)
    {
        _health.Remove(slot);
    }

    // A dead pawn has no health to read; its next life starts at its spawn.
    public void Seen(int slot, int pawnHealth)
    {
        if (pawnHealth > 0)
        {
            _health[slot] = pawnHealth;
        }
    }
}
