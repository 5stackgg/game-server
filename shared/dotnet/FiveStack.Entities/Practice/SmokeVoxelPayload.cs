namespace FiveStack.Entities.Practice;

// One smoke's voxel log as the panel takes it, either riding on a lineup's
// ingest as smoke_voxels or on its own to /utility/{id}/smoke-voxels. The
// panel hands it to the parser untouched, so this shape is the parser's
// contract and must not drift.
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

// The per-lineup upload. The panel keeps the first log a lineup gets, and only
// a render take -- the throw the preview is filmed from -- replaces it.
//
// steam_id is whoever threw it, and the panel holds them to the same rule as a
// trajectory fetch: in this server's lineup, and allowed to see the lineup.
public class SmokeVoxelUpload : SmokeVoxelPayload
{
    public string steam_id { get; set; } = "";
    public bool render { get; set; }

    public static SmokeVoxelUpload From(SmokeVoxelPayload payload, string steamId, bool render)
    {
        return new SmokeVoxelUpload
        {
            log = payload.log,
            samples = payload.samples,
            detonation = payload.detonation,
            tick_rate = payload.tick_rate,
            steam_id = steamId,
            render = render,
        };
    }
}
