using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using FiveStack.Entities.Practice;
using FiveStack.Enums;

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
        uint index = (uint)@event.Entityid;
        var position = new Vec3(@event.X, @event.Y, @event.Z);

        LineupRecord? thrown = _recorder.OnDetonated(index, position);

        // The game's own voxel log, for a throw .save may yet turn into a
        // lineup. Kept on the record, so a save that has not happened yet -- or
        // an ingest still waiting to be retried -- carries it; a save that
        // already reached the panel without it is sent it on its own.
        if (thrown != null && thrown.utility_type == nameof(eUtilityType.Smoke))
        {
            _voxels.Watch(
                index,
                position,
                $"throw by {thrown.author_steam_id}",
                payload =>
                {
                    thrown.smoke_voxels = payload;

                    if (thrown.id != null)
                    {
                        UploadSmokeVoxels(thrown.id, thrown.author_steam_id, payload);
                    }
                }
            );
        }

        return HookResult.Continue;
    }

    private void UploadSmokeVoxels(string lineupId, string steamId, SmokeVoxelPayload payload)
    {
        _ = Task.Run(() => _api.SmokeVoxels(lineupId, steamId, payload, render: false));
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
