namespace FiveStack.Entities.Practice;

// What the map itself calls its areas, as the engine has them resolved. The
// panel draws these on the radar and names utility throws from them.
//
// The published extract (web/scripts/extract-map-callouts.mjs) is the normal
// source and always wins; this exists for the maps it cannot cover -- workshop
// and community maps, where a running server is the only thing that has ever
// opened the file.
public class MapCalloutBox
{
    // Raw CS2 source units, world space, already offset by the entity's origin.
    public float[] min { get; set; } = new float[3];
    public float[] max { get; set; } = new float[3];
}

public class MapCalloutPayload
{
    public string name { get; set; } = string.Empty;

    // A place is legitimately several disjoint volumes, so the boxes travel
    // together under one name rather than as separate callouts.
    public List<MapCalloutBox> boxes { get; set; } = new List<MapCalloutBox>();
}

public class MapCalloutsPayload
{
    public string map { get; set; } = string.Empty;
    public List<MapCalloutPayload> callouts { get; set; } = new List<MapCalloutPayload>();
}
