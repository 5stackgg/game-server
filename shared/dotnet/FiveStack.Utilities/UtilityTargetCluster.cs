using FiveStack.Entities.Practice;

namespace FiveStack.Utilities;

// One place a grenade lands, and every lineup that lands there. The map draws
// targets rather than lineups because the two do not scale the same way: a
// worked map has hundreds of smokes but only a couple of dozen places worth
// smoking, and "the window smoke" is how people ask for them anyway.
public class UtilityTarget
{
    public required string Id { get; init; }
    public required Vec3 Landing { get; init; }
    public required string UtilityType { get; init; }
    public required List<LineupRecord> Lineups { get; init; }

    public int Count => Lineups.Count;

    public string Name => Lineups[0].name;
}

public static class UtilityTargetCluster
{
    // Two smokes landing this close are the same smoke as far as a player
    // choosing one is concerned. Roughly a smoke's own radius.
    public const float RadiusUnits = 150f;

    public static List<UtilityTarget> Build(
        IEnumerable<LineupRecord> lineups,
        float radius = RadiusUnits
    )
    {
        var targets = new List<UtilityTarget>();

        // Deterministic: the same library must produce the same markers in the
        // same order every frame, or they shuffle under the cursor.
        foreach (
            LineupRecord lineup in lineups
                .OrderBy(l => l.utility_type, StringComparer.Ordinal)
                .ThenBy(l => l.name, StringComparer.Ordinal)
                .ThenBy(l => l.client_id, StringComparer.Ordinal)
        )
        {
            UtilityTarget? nearest = null;
            float best = float.MaxValue;

            foreach (UtilityTarget target in targets)
            {
                // Never merge a flash into a smoke: they land together and mean
                // completely different things.
                if (
                    !string.Equals(
                        target.UtilityType,
                        lineup.utility_type,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                float distance = Distance(target.Landing, lineup.detonation_position);

                if (distance <= radius && distance < best)
                {
                    best = distance;
                    nearest = target;
                }
            }

            if (nearest != null)
            {
                nearest.Lineups.Add(lineup);

                continue;
            }

            targets.Add(
                new UtilityTarget
                {
                    Id = Key(lineup),
                    Landing = lineup.detonation_position,
                    UtilityType = lineup.utility_type,
                    Lineups = new List<LineupRecord> { lineup },
                }
            );
        }

        return targets;
    }

    // The busiest targets first, so a fixed number of markers spends them on the
    // spots the most lineups were written for.
    public static List<UtilityTarget> Top(IEnumerable<UtilityTarget> targets, int limit)
    {
        return targets
            .OrderByDescending(target => target.Count)
            .ThenBy(target => target.Name, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }

    public static string Key(LineupRecord lineup)
    {
        return string.IsNullOrEmpty(lineup.id) ? lineup.client_id : lineup.id!;
    }

    private static float Distance(Vec3 a, Vec3 b)
    {
        float dx = a.x - b.x;
        float dy = a.y - b.y;
        float dz = a.z - b.z;

        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
