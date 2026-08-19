namespace FiveStack.Entities.Practice;

// One line of a bloom outline, in world units. The renderer turns each of these
// into exactly one beam, so the number of them is the entity budget.
public struct BloomSegment
{
    public Vec3 a { get; set; }
    public Vec3 b { get; set; }

    public BloomSegment(Vec3 a, Vec3 b)
    {
        this.a = a;
        this.b = b;
    }
}
