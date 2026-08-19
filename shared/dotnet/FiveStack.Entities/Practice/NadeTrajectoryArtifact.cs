using System.Text.Json;
using FiveStack.Utilities;

namespace FiveStack.Entities.Practice;

// What GET /nades/{id}/trajectory answers with.
//
// The artifact is deliberately the same top-level shape as the demo playback
// blob, so the flight path is nested under grenade_trajectories rather than
// being the document. Earlier, flatter spellings are still read: the API owns
// the contract, and a preview that silently draws nothing is the worst way to
// discover it moved.
public class NadeTrajectoryArtifact
{
    public List<TrajectoryPoint> path { get; set; } = new List<TrajectoryPoint>();
    public SmokeVolume? smoke_volume { get; set; }

    public static NadeTrajectoryArtifact Parse(byte[] body)
    {
        return Parse(PracticeJson.Text(body));
    }

    public static NadeTrajectoryArtifact Parse(string body)
    {
        var artifact = new NadeTrajectoryArtifact();

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            artifact.path = Points(root);
            return artifact;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return artifact;
        }

        artifact.path = PathFrom(root);
        artifact.smoke_volume = VolumeFrom(root);

        return artifact;
    }

    private static List<TrajectoryPoint> PathFrom(JsonElement root)
    {
        foreach (string property in new[] { "path", "trajectory", "points" })
        {
            if (Array(root, property, out JsonElement flat))
            {
                return Points(flat);
            }
        }

        if (!Array(root, "grenade_trajectories", out JsonElement trajectories))
        {
            return new List<TrajectoryPoint>();
        }

        foreach (JsonElement trajectory in trajectories.EnumerateArray())
        {
            if (trajectory.ValueKind == JsonValueKind.Object && Array(trajectory, "points", out JsonElement points))
            {
                return Points(points);
            }
        }

        return new List<TrajectoryPoint>();
    }

    private static SmokeVolume? VolumeFrom(JsonElement root)
    {
        if (
            root.TryGetProperty("smoke_volume", out JsonElement single)
            && single.ValueKind == JsonValueKind.Object
        )
        {
            return Volume(single);
        }

        if (!Array(root, "smoke_volumes", out JsonElement volumes))
        {
            return null;
        }

        foreach (JsonElement volume in volumes.EnumerateArray())
        {
            if (volume.ValueKind == JsonValueKind.Object)
            {
                return Volume(volume);
            }
        }

        return null;
    }

    private static SmokeVolume? Volume(JsonElement element)
    {
        var volume = new SmokeVolume
        {
            ox = Number(element, "ox"),
            oy = Number(element, "oy"),
            oz = Number(element, "oz"),
            vs = Number(element, "vs"),
            dx = (int)Number(element, "dx"),
            dy = (int)Number(element, "dy"),
            dz = (int)Number(element, "dz"),
            den = Text(element, "den"),
        };

        // A grid with no extent is not a measurement, and treating it as one
        // draws a bloom outline collapsed onto a point.
        if (volume.vs <= 0f || volume.dx <= 0 || volume.dy <= 0 || volume.dz <= 0)
        {
            return null;
        }

        return volume;
    }

    private static List<TrajectoryPoint> Points(JsonElement array)
    {
        var points = new List<TrajectoryPoint>();

        foreach (JsonElement point in array.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            points.Add(
                new TrajectoryPoint
                {
                    p = new Vec3(Number(point, "x"), Number(point, "y"), Number(point, "z")),
                    t = (int)Number(point, "tick"),
                }
            );
        }

        return points;
    }

    private static bool Array(JsonElement element, string property, out JsonElement value)
    {
        return element.TryGetProperty(property, out value)
            && value.ValueKind == JsonValueKind.Array;
    }

    private static float Number(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out double parsed)
            ? (float)parsed
            : 0f;
    }

    private static string? Text(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
