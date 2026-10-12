using FiveStack.Entities.Practice;
using FiveStack.Enums;
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
        uint index = EntityIndex(@event.EntityID);
        var position = new Vec3(@event.X, @event.Y, @event.Z);

        // Both asked first: Detonated is where the replay forgets its ghost.
        (LineupRecord lineup, ulong steamId)? rendered = _director.TakeLineup(index);
        (string lineupId, ulong steamId)? replayed = _replay.GhostLineup(index);

        LineupRecord? thrown = Detonated(index, position);

        WatchSmoke(index, position, thrown, rendered, replayed);

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

        if (_director.OnMolotovDetonated(thrower.SteamID, position))
        {
            return HookResult.Continue;
        }

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
    // ever lands in one of them. Answers the recorder's throw, if it was one.
    private LineupRecord? Detonated(uint entityIndex, Vec3 position)
    {
        LineupRecord? thrown = _recorder.OnDetonated(entityIndex, position);
        _solver.OnDetonated(entityIndex, position);
        _director.OnDetonated(entityIndex, position);
        Signal(_replay.GhostDetonated(entityIndex, position));

        return thrown;
    }

    // The game's own voxel log for a smoke somebody can use: a render take,
    // a replay of a saved lineup, or a player's own throw, which .save may yet
    // turn into one. Anything else -- the bloom preview above all, which is
    // emitted at the landing point and is no throw at all -- is left alone.
    private void WatchSmoke(
        uint index,
        Vec3 position,
        LineupRecord? thrown,
        (LineupRecord lineup, ulong steamId)? rendered,
        (string lineupId, ulong steamId)? replayed
    )
    {
        if (rendered?.lineup.id is string renderedId)
        {
            string pod = rendered.Value.steamId.ToString();

            _voxels.Watch(
                index,
                position,
                $"[nade-render] render of {renderedId} by {pod}",
                payload => UploadSmokeVoxels(renderedId, pod, payload, render: true)
            );
        }

        if (replayed is (string replayedId, ulong replayer))
        {
            string by = replayer.ToString();

            _voxels.Watch(
                index,
                position,
                $"[nade-render] replay of {replayedId} for {by}",
                payload => UploadSmokeVoxels(replayedId, by, payload, render: false)
            );
        }

        if (thrown != null && thrown.utility_type == nameof(eUtilityType.Smoke))
        {
            _voxels.Watch(
                index,
                position,
                $"throw by {thrown.author_steam_id}",
                payload => ThrowSmokeVoxels(thrown, payload)
            );
        }
    }

    // Kept on the throw, so a .save that has not happened yet -- or an ingest
    // still waiting to be retried -- carries it. A save that already reached
    // the panel without it is sent it on its own; see SaveThrow for the save
    // that is still on its way.
    private void ThrowSmokeVoxels(LineupRecord thrown, SmokeVoxelPayload payload)
    {
        thrown.smoke_voxels = payload;

        if (thrown.id != null)
        {
            UploadSmokeVoxels(thrown.id, thrown.author_steam_id, payload, render: false);
        }
    }

    private void UploadSmokeVoxels(
        string lineupId,
        string steamId,
        SmokeVoxelPayload payload,
        bool render
    )
    {
        _ = Task.Run(() => _api.SmokeVoxels(lineupId, steamId, payload, render));
    }

    // Swiftly types the field as a signed short; the unsigned round trip keeps
    // a high index from arriving as a negative number.
    private static uint EntityIndex(short entityId)
    {
        return (ushort)entityId;
    }
}
