using System.Runtime.InteropServices;
using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace UtilityPractice;

// Reads a smoke's real voxel log off its projectile while the cloud grows, so
// the panel can show the cloud the game made instead of an estimate of it.
//
// Only smokes somebody can use are watched -- a player's own throw, which
// .save may turn into a lineup, and a replay of a saved one -- and only a few
// at once. When the log is finished is SmokeVoxelCapture's call; this only
// reads the fields.
public class SmokeVoxelRecorder
{
    // A practice server is a handful of people. More clouds than this growing
    // at once is somebody spamming smokes, and those are not lineups.
    private const int MaxWatched = 8;

    private readonly ISwiftlyCore _core;
    private readonly ILogger<SmokeVoxelRecorder> _logger;

    private sealed class Reading
    {
        public required CSmokeGrenadeProjectile Smoke;
        public required uint Handle;
        public required uint Index;
        public required int DetonationTick;
        public required string Label;
        public required Vec3 Landing;
        public required Action<SmokeVoxelPayload> Finished;
        public readonly SmokeVoxelCapture Capture = new SmokeVoxelCapture();
        public bool Short;
    }

    private readonly List<Reading> _reading = new List<Reading>();

    private int _tick;

    public SmokeVoxelRecorder(ISwiftlyCore core, ILogger<SmokeVoxelRecorder> logger)
    {
        _core = core;
        _logger = logger;
    }

    // Called from the detonate event. A smoke can be two things at once -- a
    // replay the recorder also took for a throw -- so a second caller joins
    // the first rather than reading the same log twice.
    public void Watch(
        uint entityIndex,
        Vec3 landing,
        string label,
        Action<SmokeVoxelPayload> finished
    )
    {
        foreach (Reading open in _reading)
        {
            if (open.Index == entityIndex)
            {
                open.Finished += finished;
                return;
            }
        }

        if (_reading.Count >= MaxWatched)
        {
            _logger.LogWarning(
                "no smoke voxels for {label}: already reading {count} smokes",
                label,
                _reading.Count
            );
            return;
        }

        CSmokeGrenadeProjectile? smoke = SmokeAt(entityIndex);
        uint? handle = smoke?.Entity?.EntityHandle.Raw;

        if (smoke == null || handle == null)
        {
            return;
        }

        var reading = new Reading
        {
            Smoke = smoke,
            Handle = handle.Value,
            Index = entityIndex,
            // The event fires inside the detonation tick and OnTick at its end,
            // so that first read is tick 0, as it is in a demo.
            DetonationTick = _tick + 1,
            Label = label,
            Landing = landing,
            Finished = finished,
        };

        _reading.Add(reading);

        // The detonation frame can already be in the log.
        Read(reading);
    }

    public void OnTick()
    {
        _tick++;

        for (int index = _reading.Count - 1; index >= 0; index--)
        {
            Reading reading = _reading[index];
            bool alive = Read(reading);
            eSmokeVoxelEnd end = reading.Capture.Check(Since(reading));

            if (alive && end == eSmokeVoxelEnd.Growing)
            {
                continue;
            }

            _reading.RemoveAt(index);
            Finish(reading, alive ? end.ToString() : "projectile gone");
        }
    }

    // Dropped, not finished: nothing is left to send them by.
    public void Reset()
    {
        _reading.Clear();
    }

    private int Since(Reading reading)
    {
        return Math.Max(0, _tick - reading.DetonationTick);
    }

    // False once the projectile is gone.
    private bool Read(Reading reading)
    {
        CSmokeGrenadeProjectile smoke = reading.Smoke;

        // The handle as well as the address: a freed projectile's memory can be
        // handed to the next entity, and its fields read as this one's.
        if (!smoke.IsValid || smoke.Entity?.EntityHandle.Raw != reading.Handle)
        {
            return false;
        }

        Vector at = smoke.SmokeDetonationPos;

        if (at.X != 0f || at.Y != 0f || at.Z != 0f)
        {
            reading.Capture.Detonation = new Vec3(at.X, at.Y, at.Z);
        }

        // The size is what the game says is in use; the vector's own count is
        // what can be read without walking off the end of it.
        ref CUtlVector<byte> data = ref smoke.VoxelFrameData;
        int size = smoke.VoxelFrameDataSize;
        int readable = Math.Min(size, data.Count);

        if (readable < size && !reading.Short)
        {
            reading.Short = true;
            _logger.LogWarning(
                "smoke voxels for {label}: the log says {size} bytes but holds {count}",
                reading.Label,
                size,
                data.Count
            );
        }

        int grown = reading.Capture.Growth(readable);

        if (grown == 0 || data.Base == 0)
        {
            return true;
        }

        byte[] chunk = new byte[grown];
        Marshal.Copy(data.Base + reading.Capture.Length, chunk, 0, grown);
        reading.Capture.Append(chunk, Since(reading));

        return true;
    }

    private void Finish(Reading reading, string reason)
    {
        SmokeVoxelCapture capture = reading.Capture;

        if (capture.Detonation.Length() == 0f)
        {
            capture.Detonation = reading.Landing;
        }

        _logger.LogInformation(
            "{label}: smoke voxels {bytes} bytes, {samples} samples over {seconds:0.00}s ({reason})",
            reading.Label,
            capture.Length,
            capture.Samples,
            Since(reading) / (float)SmokeVoxelCapture.TickRate,
            reason
        );

        SmokeVoxelPayload? payload = capture.ToPayload();

        if (payload == null)
        {
            return;
        }

        try
        {
            reading.Finished(payload);
        }
        catch (Exception error)
        {
            _logger.LogError(
                error,
                "unable to hand off the smoke voxels for {label}",
                reading.Label
            );
        }
    }

    private CSmokeGrenadeProjectile? SmokeAt(uint index)
    {
        try
        {
            CEntityInstance? entity = _core.EntitySystem.GetEntityByIndex(index);

            if (
                entity == null
                || !entity.IsValid
                || entity.DesignerName != "smokegrenade_projectile"
            )
            {
                return null;
            }

            return entity.As<CSmokeGrenadeProjectile>();
        }
        catch
        {
            return null;
        }
    }
}
