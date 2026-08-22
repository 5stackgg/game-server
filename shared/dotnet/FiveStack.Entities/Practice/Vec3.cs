namespace FiveStack.Entities.Practice;

// Shared code is compiled into every plugin app and must not reference either
// game framework, so positions cross this boundary as plain floats rather than
// CounterStrikeSharp's Vector or Swiftly's.
public struct Vec3
{
    public float x { get; set; }
    public float y { get; set; }
    public float z { get; set; }

    public Vec3(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public float Length()
    {
        return MathF.Sqrt((x * x) + (y * y) + (z * z));
    }

    public float LengthXY()
    {
        return MathF.Sqrt((x * x) + (y * y));
    }

    public float Dot(Vec3 other)
    {
        return (x * other.x) + (y * other.y) + (z * other.z);
    }

    public Vec3 Normalized()
    {
        float length = Length();

        return length <= float.Epsilon ? new Vec3(0f, 0f, 0f) : this * (1f / length);
    }

    public static Vec3 operator -(Vec3 a, Vec3 b)
    {
        return new Vec3(a.x - b.x, a.y - b.y, a.z - b.z);
    }

    public static Vec3 operator +(Vec3 a, Vec3 b)
    {
        return new Vec3(a.x + b.x, a.y + b.y, a.z + b.z);
    }

    public static Vec3 operator *(Vec3 a, float scale)
    {
        return new Vec3(a.x * scale, a.y * scale, a.z * scale);
    }
}
