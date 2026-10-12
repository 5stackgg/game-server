namespace FiveStack.Entities.Practice;

// One smoke's voxel log as the panel takes it, riding on a lineup's ingest as
// smoke_voxels. The panel hands it to the parser untouched, so this shape is
// the parser's contract and must not drift.
//
// log is base64 of the bytes as they finally stood. samples are [byte length,
// ticks after detonation], one per tick the log grew, ascending, the last one
// covering the whole log: the parser rebuilds the cloud's growth from when each
// frame arrived, not just from what arrived.
public class SmokeVoxelPayload
{
    public string log { get; set; } = "";
    public List<int[]> samples { get; set; } = new List<int[]>();
    public Vec3 detonation { get; set; }
    public int tick_rate { get; set; }
}
