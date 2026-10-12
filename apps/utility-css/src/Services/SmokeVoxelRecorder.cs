using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace UtilityPractice;

// Reads a smoke's real voxel log off its projectile while the cloud grows, so
// the panel can show the cloud the game made instead of an estimate of it.
//
// Only a player's own smoke is watched here -- the one .save may turn into a
// lineup -- and only a few at once. When the log is finished is
// SmokeVoxelCapture's call; this only reads the fields.
public class SmokeVoxelRecorder
{
    // A practice server is a handful of people. More clouds than this growing
    // at once is somebody spamming smokes, and those are not lineups.
    private const int MaxWatched = 8;

    // CounterStrikeSharp's NetworkedVector only indexes handles, so the log is
    // read straight out of the CUtlVector: the element count first, the
    // element pointer 8 bytes in.
    private const int VectorMemoryOffset = 8;

    private readonly ILogger<SmokeVoxelRecorder> _logger;

    private sealed class Reading
    {
        public required CSmokeGrenadeProjectile Smoke;
        public required int DetonationTick;
        public required string Label;
        public required Vec3 Landing;
        public required Action<SmokeVoxelPayload?> Finished;
        public readonly SmokeVoxelCapture Capture = new SmokeVoxelCapture();
        public bool Short;
    }

    private readonly List<Reading> _reading = new List<Reading>();

    private int _tick;
    private int? _vectorOffset;

    public SmokeVoxelRecorder(ILogger<SmokeVoxelRecorder> logger)
    {
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

        CSmokeGrenadeProjectile? smoke = Utilities.GetEntityFromIndex<CSmokeGrenadeProjectile>(
            (int)entityIndex
        );

        if (smoke == null || !smoke.IsValid || smoke.DesignerName != "smokegrenade_projectile")
        {
            return false;
        }

        var reading = new Reading
        {
            Smoke = smoke,
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

    // False once the projectile is gone. The wrapper resolves through the
    // entity handle, so a slot reused by another entity reads as gone.
    private bool Read(Reading reading)
    {
        CSmokeGrenadeProjectile smoke = reading.Smoke;
        nint entity = smoke.Handle;

        if (!smoke.IsValid || entity == IntPtr.Zero)
        {
            return false;
        }

        // A Vector here is an allocation, so it is only read until it is set.
        if (reading.Capture.Detonation.Length() == 0f)
        {
            Vector at = smoke.SmokeDetonationPos;
            reading.Capture.Detonation = new Vec3(at.X, at.Y, at.Z);
        }

        _vectorOffset ??= Schema.GetSchemaOffset("CSmokeGrenadeProjectile", "m_VoxelFrameData");

        nint vector = entity + _vectorOffset.Value;
        int count = Marshal.ReadInt32(vector);
        nint memory = Marshal.ReadIntPtr(vector, VectorMemoryOffset);

        // The size is what the game says is in use; the vector's own count is
        // what can be read without walking off the end of it.
        int size = smoke.VoxelFrameDataSize;
        int readable = Math.Min(size, count);

        if (readable < size && !reading.Short)
        {
            reading.Short = true;
            _logger.LogWarning(
                "smoke voxels for {label}: the log says {size} bytes but holds {count}",
                reading.Label,
                size,
                count
            );
        }

        int grown = reading.Capture.Growth(readable);

        if (grown == 0 || memory == IntPtr.Zero)
        {
            return true;
        }

        byte[] chunk = new byte[grown];
        Marshal.Copy(memory + reading.Capture.Length, chunk, 0, grown);
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
}
