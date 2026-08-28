using System.Text.Json;
using FiveStack.Enums;

namespace FiveStack.Utilities;

// The radar calibration the HUD projects with, mirrored from the web panel by
// scripts/sync-radars.sh. Only maps listed here can draw a minimap; everything
// else falls back to the list.
public static class RadarMaps
{
    // Kept in step with the stylesheet's #radar.<map> rules and the images in
    // panorama/images/nadehud. HudLayoutContractTests asserts all three agree.
    public static readonly IReadOnlyList<string> All = new[]
    {
        "de_ancient",
        "de_anubis",
        "de_cache",
        "de_dust2",
        "de_inferno",
        "de_mirage",
        "de_nuke",
        "de_overpass",
        "de_train",
        "de_vertigo",
    };

    // Marker colour classes. The engine's spelling for a molotov is Molotov and
    // for an HE is HighExplosive; the stylesheet uses short names.
    public static readonly IReadOnlyList<string> Types = new[] { "smoke", "flash", "molly", "he" };

    public static string TypeClass(string? utilityType)
    {
        if (string.Equals(utilityType, nameof(eUtilityType.Smoke), StringComparison.OrdinalIgnoreCase))
        {
            return "smoke";
        }

        if (string.Equals(utilityType, nameof(eUtilityType.Flash), StringComparison.OrdinalIgnoreCase))
        {
            return "flash";
        }

        if (string.Equals(utilityType, nameof(eUtilityType.Molotov), StringComparison.OrdinalIgnoreCase))
        {
            return "molly";
        }

        return "he";
    }

    public static bool Has(string? map)
    {
        return All.Contains(RadarProjection.NormalizeMapName(map));
    }

    // metadata.json ships beside the layouts; a missing or malformed file means
    // no minimap rather than a broken one.
    public static Dictionary<string, RadarCalibration> Load(string path)
    {
        var calibrations = new Dictionary<string, RadarCalibration>(StringComparer.Ordinal);

        if (!File.Exists(path))
        {
            return calibrations;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));

        foreach (JsonProperty entry in document.RootElement.EnumerateObject())
        {
            if (entry.Name.StartsWith("_", StringComparison.Ordinal))
            {
                continue;
            }

            RadarCalibration? parsed = Parse(entry.Value);

            if (parsed != null)
            {
                calibrations[entry.Name] = parsed;
            }
        }

        return calibrations;
    }

    private static RadarCalibration? Parse(JsonElement element)
    {
        if (
            !element.TryGetProperty("resolution", out JsonElement resolution)
            || !element.TryGetProperty("offset", out JsonElement offset)
            || !offset.TryGetProperty("x", out JsonElement offsetX)
            || !offset.TryGetProperty("y", out JsonElement offsetY)
        )
        {
            return null;
        }

        var calibration = new RadarCalibration
        {
            Resolution = resolution.GetSingle(),
            OffsetX = offsetX.GetSingle(),
            OffsetY = offsetY.GetSingle(),
        };

        if (element.TryGetProperty("splits", out JsonElement splits))
        {
            foreach (JsonElement split in splits.EnumerateArray())
            {
                if (
                    !split.TryGetProperty("bounds", out JsonElement bounds)
                    || !split.TryGetProperty("offset", out JsonElement splitOffset)
                )
                {
                    continue;
                }

                calibration.Splits.Add(
                    new RadarSplit
                    {
                        BoundsTop = bounds.GetProperty("top").GetSingle(),
                        BoundsBottom = bounds.GetProperty("bottom").GetSingle(),
                        OffsetX = splitOffset.GetProperty("x").GetSingle(),
                        OffsetY = splitOffset.GetProperty("y").GetSingle(),
                    }
                );
            }
        }

        return calibration;
    }
}
