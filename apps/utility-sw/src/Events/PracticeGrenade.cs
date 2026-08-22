using FiveStack.Entities.Practice;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace UtilityPractice;

// Detonation is the other half of a recording: the projectile stops existing
// and we finally know where the lineup lands.
//
// Every event here except the molotov carries the projectile's entity index, so
// the throw it belongs to is a dictionary lookup. EventMolotovDetonate carries
// only the thrower, which is why it takes the thrower-based path.
public partial class UtilityPracticePlugin
{
    [GameEventHandler(HookMode.Post)]
    public HookResult OnSmokeDetonate(EventSmokegrenadeDetonate @event)
    {
        Detonated(EntityIndex(@event.EntityID), new Vec3(@event.X, @event.Y, @event.Z));
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnFlashDetonate(EventFlashbangDetonate @event)
    {
        Detonated(EntityIndex(@event.EntityID), new Vec3(@event.X, @event.Y, @event.Z));
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnHeDetonate(EventHegrenadeDetonate @event)
    {
        Detonated(EntityIndex(@event.EntityID), new Vec3(@event.X, @event.Y, @event.Z));
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnDecoyStarted(EventDecoyStarted @event)
    {
        Detonated(EntityIndex(@event.EntityID), new Vec3(@event.X, @event.Y, @event.Z));
        return HookResult.Continue;
    }

    // No entity id on this one -- the thrower is the only handle we get.
    [GameEventHandler(HookMode.Post)]
    public HookResult OnMolotovDetonate(EventMolotovDetonate @event)
    {
        // Without an entity index there is no way to tell a solver molotov from
        // the thrower's own, and guessing wrong finalizes a real lineup at the
        // solver's landing point.
        IPlayer? thrower = @event.UserIdPlayer;
        if (thrower == null || !thrower.IsValid || _solver.EmittingMolotovs)
        {
            return HookResult.Continue;
        }

        var position = new Vec3(@event.X, @event.Y, @event.Z);

        // The replay is asked first on purpose. While one of this player's
        // ghosts is in the air the two molotovs cannot be told apart, and
        // handing the ghost's landing point to the recorder finalizes the throw
        // they actually made at a spot it never reached -- a hit nobody threw.
        // Left alone, the real projectile finalizes off the recorder's own
        // sampling a tick after it goes out, which is where it really landed.
        string? ghost = _replay.GhostMolotovDetonated(thrower.SteamID, position);

        if (ghost != null)
        {
            Signal(ghost);
            return HookResult.Continue;
        }

        _recorder.OnMolotovDetonated(thrower.SteamID, position);

        return HookResult.Continue;
    }

    // The three readers of a detonation. The recorder ignores an index it never
    // tracked and the solver ignores one it never emitted, so a projectile only
    // ever lands in one of them.
    private void Detonated(uint entityIndex, Vec3 position)
    {
        _recorder.OnDetonated(entityIndex, position);
        _solver.OnDetonated(entityIndex, position);
        Signal(_replay.GhostDetonated(entityIndex, position));
    }

    // Swiftly types the field as a signed short; the unsigned round trip keeps
    // a high index from arriving as a negative number.
    private static uint EntityIndex(short entityId)
    {
        return (ushort)entityId;
    }
}
