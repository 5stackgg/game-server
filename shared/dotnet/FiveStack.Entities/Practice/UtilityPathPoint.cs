namespace FiveStack.Entities.Practice;

// One sample of a flight path, in the API's spelling. Deliberately an object
// per point rather than a packed array: the panel validates and stores these
// field by field, and it is the API that owns the wire contract.
public class UtilityPathPoint
{
    public int? tick { get; set; }
    public float? x { get; set; }
    public float? y { get; set; }
    public float? z { get; set; }
}
