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
// Only a player's own smoke is watched -- the one .save may turn into a lineup
// -- or a render take's, and only a few at once. When the log is finished is SmokeVoxelCapture's
// call; this only reads the fields.
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
        public required int DetonationTick;
        public required string Label;
        public required Vec3 Landing;
        public required Action<SmokeVoxelPayload?> Finished;
        public readonly SmokeVoxelCapture Capture = new SmokeVoxelCapture();
        public bool Short;
    }

    private readonly List<Reading> _reading = new List<Reading>();

    // A cloud is still growing, and its log is lost if its projectile goes now.
    public bool IsReading => _reading.Count > 0;

    private int _tick;

    public SmokeVoxelRecorder(ISwiftlyCore core, ILogger<SmokeVoxelRecorder> logger)
    {
        _core = core;
        _logger = logger;
    }

    // Called from the detonate event. finished is always called once the
    // reading ends -- with null for a cloud cut short or that wrote nothing --
    // so whoever is waiting on it is never left waiting. False, and finished
    // is never called, when the smoke is not read at all.
    public bool Watch(
        uint entityIndex,
        Vec3 landing,
        string label,
        Action<SmokeVoxelPayload?> finished
    )
    {
        if (_reading.Count >= MaxWatched)
        {
            _logger.LogWarning(
                "no smoke voxels for {label}: already reading {count} smokes",
                label,
                _reading.Count
            );
            return false;
        }

        CSmokeGrenadeProjectile? smoke = SmokeAt(entityIndex);
        uint? handle = smoke?.Entity?.EntityHandle.Raw;

        if (smoke == null || handle == null)
        {
            return false;
        }

        var reading = new Reading
        {
            Smoke = smoke,
            Handle = handle.Value,
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

        return true;
    }

    public void OnTick()
    {
        _tick++;

        for (int index = _reading.Count - 1; index >= 0; index--)
        {
            Reading reading = _reading[index];

            if (!Read(reading))
            {
                reading.Capture.CutShort();
            }
            else if (reading.Capture.Check(Since(reading)) == eSmokeVoxelEnd.Growing)
            {
                continue;
            }

            _reading.RemoveAt(index);
            Finish(reading);
        }
    }

    // A map change or an unload: every cloud still growing is cut short, and
    // its owner hears so now rather than waiting on a projectile that is gone.
    public void Reset()
    {
        List<Reading> cut = new List<Reading>(_reading);
        _reading.Clear();

        foreach (Reading reading in cut)
        {
            reading.Capture.CutShort();
            Finish(reading);
        }
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

    private void Finish(Reading reading)
    {
        SmokeVoxelCapture capture = reading.Capture;

        if (capture.Detonation.Length() == 0f)
        {
            capture.Detonation = reading.Landing;
        }

        _logger.LogInformation(
            "{label}: smoke voxels {bytes} bytes, {samples} samples over {seconds:0.00}s ({end})",
            reading.Label,
            capture.Length,
            capture.Samples,
            Since(reading) / (float)SmokeVoxelCapture.TickRate,
            capture.End
        );

        try
        {
            reading.Finished(capture.ToPayload());
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
