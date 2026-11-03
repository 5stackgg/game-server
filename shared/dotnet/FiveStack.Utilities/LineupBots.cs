using FiveStack.Entities.Practice;

namespace FiveStack.Utilities;

public static class LineupBots
{
    // Closer than this to a saved spot and a standing bot is already that spot's.
    public const float SameSpotUnits = 32f;

    // Waiting counts the lineups a press could mean when it cannot pick one.
    public readonly record struct Choice(LineupRecord? Lineup, int Waiting);

    public static Choice Pick(LineupRecord? focused, IReadOnlyList<LineupRecord> here)
    {
        if (focused != null && focused.bots.Count > 0)
        {
            return new Choice(focused, 0);
        }

        List<LineupRecord> withBots = here.Where(lineup => lineup.bots.Count > 0).ToList();

        return withBots.Count == 1 ? new Choice(withBots[0], 0) : new Choice(null, withBots.Count);
    }

    public static bool SpotsSurviveLoad(string fromMap, string toMap)
    {
        return fromMap == toMap;
    }

    public static List<LineupBot> Missing(
        IReadOnlyList<LineupBot> saved,
        IReadOnlyList<ThrowSnapshot> standing
    )
    {
        return saved
            .Where(bot =>
                !standing.Any(spot =>
                    (new Vec3(bot.x, bot.y, bot.z) - spot.feet_position).Length() <= SameSpotUnits
                )
            )
            .ToList();
    }

    public static List<LineupBot> Snapshot(IReadOnlyList<ThrowSnapshot> standing, int max)
    {
        return standing
            .Take(max)
            .Select(spot => new LineupBot
            {
                x = spot.feet_position.x,
                y = spot.feet_position.y,
                z = spot.feet_position.z,
                yaw = spot.yaw,
            })
            .ToList();
    }
}
