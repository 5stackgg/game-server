using FiveStack.Entities.Practice;

namespace FiveStack.Utilities;

public enum eSmokeVoxelEnd
{
    Growing,

    // Nothing new for QuietTicks: the cloud has finished spreading.
    Settled,

    // The networked array is full, so there is nothing left to read.
    Full,

    // Still growing at MaxTicks. What arrived is kept; a cloud that slow is
    // not going to finish into anything better.
    TimedOut,

    // The projectile went first -- cleared, a round restart, a map change.
    // The panel keeps the first cloud a lineup gets, so a half-grown one is
    // never handed over.
    CutShort,
}

// One smoke's voxel log, read off its projectile while the cloud grows.
//
// CS2 networks each smoke's own simulation on CSmokeGrenadeProjectile as an
// append-only byte log: m_VoxelFrameData, m_nVoxelFrameDataSize bytes of it in
// use. The parser that rebuilds the cloud needs both halves of what a demo
// gives it -- the bytes, and the tick each one arrived -- so every growth is
// appended here and stamped [size, ticks since detonation].
//
// Engine-free: the plugin reads the two fields once a tick and this decides
// everything else, which is what lets it be tested without a server.
public sealed class SmokeVoxelCapture
{
    // The length of the networked array.
    public const int MaxBytes = 3072;

    public const int TickRate = 64;

    // A cloud is done spreading about four seconds in.
    public const int QuietTicks = 3 * TickRate;
    public const int MaxTicks = 8 * TickRate;

    private readonly byte[] _log = new byte[MaxBytes];
    private readonly List<int[]> _samples = new List<int[]>();

    // Detonation counts as growth, so a smoke that never writes a byte still
    // finishes after QuietTicks rather than holding a slot until MaxTicks.
    private int _grewAt;

    public int Length { get; private set; }

    public int Samples => _samples.Count;

    // Final once it is anything but Growing.
    public eSmokeVoxelEnd End { get; private set; }

    // Where the game centres the grid, which is not quite the event's landing
    // point. Zero until the projectile says.
    public Vec3 Detonation { get; set; }

    // How many bytes past what is held a reported size means. The log only
    // ever grows, so a smaller size is nothing new rather than a rewind.
    public int Growth(int size)
    {
        return Math.Max(0, Math.Min(size, MaxBytes) - Length);
    }

    public void Append(ReadOnlySpan<byte> bytes, int tick)
    {
        int count = Math.Min(bytes.Length, MaxBytes - Length);

        if (count <= 0)
        {
            return;
        }

        bytes.Slice(0, count).CopyTo(_log.AsSpan(Length));
        Length += count;

        int[]? last = _samples.Count > 0 ? _samples[^1] : null;
        tick = Math.Max(tick, last?[1] ?? 0);

        // Two reads on one tick are one arrival as far as the parser's clock
        // can tell.
        if (last != null && last[1] == tick)
        {
            last[0] = Length;
        }
        else
        {
            _samples.Add(new[] { Length, tick });
        }

        _grewAt = tick;
    }

    public eSmokeVoxelEnd Check(int tick)
    {
        if (End != eSmokeVoxelEnd.Growing)
        {
            return End;
        }

        if (Length >= MaxBytes)
        {
            End = eSmokeVoxelEnd.Full;
        }
        else if (tick - _grewAt >= QuietTicks)
        {
            End = eSmokeVoxelEnd.Settled;
        }
        else if (tick >= MaxTicks)
        {
            End = eSmokeVoxelEnd.TimedOut;
        }

        return End;
    }

    public void CutShort()
    {
        if (End == eSmokeVoxelEnd.Growing)
        {
            End = eSmokeVoxelEnd.CutShort;
        }
    }

    // Only a cloud that finished growing, and only one that wrote something:
    // an empty log has no cloud in it to rebuild.
    public SmokeVoxelPayload? ToPayload()
    {
        if (Length == 0 || End == eSmokeVoxelEnd.Growing || End == eSmokeVoxelEnd.CutShort)
        {
            return null;
        }

        return new SmokeVoxelPayload
        {
            log = Convert.ToBase64String(_log, 0, Length),
            samples = _samples.Select(sample => new[] { sample[0], sample[1] }).ToList(),
            detonation = Detonation,
            tick_rate = TickRate,
        };
    }
}
