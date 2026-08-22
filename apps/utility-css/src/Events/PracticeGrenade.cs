using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using FiveStack.Entities.Practice;

namespace UtilityPractice;

// Detonation is the other half of a recording: the projectile stops existing
// and we finally know where the lineup lands.
//
// Every event here except the molotov carries the projectile's entity index, so
// the throw it belongs to is a dictionary lookup. EventMolotovDetonate carries
// only Userid, which is why it takes the thrower-based path.
public partial class UtilityPracticePlugin
{
    [GameEventHandler]
    public HookResult OnSmokeDetonate(EventSmokegrenadeDetonate @event, GameEventInfo info)
    {
        _recorder.OnDetonated(
            (uint)@event.Entityid,
            new Vec3(@event.X, @event.Y, @event.Z)
        );
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnFlashDetonate(EventFlashbangDetonate @event, GameEventInfo info)
    {
        _recorder.OnDetonated(
            (uint)@event.Entityid,
            new Vec3(@event.X, @event.Y, @event.Z)
        );
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnHeDetonate(EventHegrenadeDetonate @event, GameEventInfo info)
    {
        _recorder.OnDetonated(
            (uint)@event.Entityid,
            new Vec3(@event.X, @event.Y, @event.Z)
        );
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnDecoyStarted(EventDecoyStarted @event, GameEventInfo info)
    {
        _recorder.OnDetonated(
            (uint)@event.Entityid,
            new Vec3(@event.X, @event.Y, @event.Z)
        );
        return HookResult.Continue;
    }

    // No Entityid on this one -- the thrower is the only handle we get.
    [GameEventHandler]
    public HookResult OnMolotovDetonate(EventMolotovDetonate @event, GameEventInfo info)
    {
        CCSPlayerController? thrower = @event.Userid;
        if (thrower == null || !thrower.IsValid)
        {
            return HookResult.Continue;
        }

        _recorder.OnMolotovDetonated(
            thrower.SteamID,
            new Vec3(@event.X, @event.Y, @event.Z)
        );
        return HookResult.Continue;
    }
}
