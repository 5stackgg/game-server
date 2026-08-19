using FiveStack.Utilities;

namespace FiveStack.Entities.Practice;

// The body of POST /utility/ingest, exactly as the API defines it.
//
// LineupRecord stays the plugin's own model; this flat shape is the only thing
// that crosses the wire, because it maps one to one onto the columns the panel
// writes. Anything the payload does not name is dropped rather than sent: the
// map in particular is derived from the authenticated server's own match row,
// and sending our own would be rejected as a mismatch.
public class UtilityIngestPayload
{
    public string? match_id { get; set; }
    public string? author_steam_id { get; set; }
    public string? utility_type { get; set; }
    public string? side { get; set; }
    public string? technique { get; set; }
    public string? throw_strength { get; set; }
    public bool? jump_throw_bind { get; set; }

    public float? origin_x { get; set; }
    public float? origin_y { get; set; }
    public float? origin_z { get; set; }
    public float? eye_z { get; set; }

    public float? view_yaw { get; set; }
    public float? view_pitch { get; set; }

    public float? land_x { get; set; }
    public float? land_y { get; set; }
    public float? land_z { get; set; }

    public int? flight_time_ms { get; set; }
    public string? name { get; set; }
    public string? description { get; set; }
    public int? tick_rate { get; set; }

    public List<UtilityPathPoint>? path { get; set; }

    public static UtilityIngestPayload From(LineupRecord lineup)
    {
        return new UtilityIngestPayload
        {
            author_steam_id = Text(lineup.author_steam_id),
            utility_type = PracticeLineupUtility.NormalizeUtilityType(lineup.utility_type),
            side = Text(lineup.side),
            technique = Text(lineup.technique),
            throw_strength = Text(lineup.strength),
            jump_throw_bind = lineup.release.jump_throw,

            origin_x = lineup.release.feet_position.x,
            origin_y = lineup.release.feet_position.y,
            origin_z = lineup.release.feet_position.z,
            eye_z = lineup.release.eye_position.z,

            view_yaw = lineup.release.yaw,
            view_pitch = lineup.release.pitch,

            land_x = lineup.detonation_position.x,
            land_y = lineup.detonation_position.y,
            land_z = lineup.detonation_position.z,

            flight_time_ms = MillisecondsFromSeconds(lineup.flight_time),
            name = Text(lineup.name),
            tick_rate = lineup.recorded_tickrate,

            path = lineup
                .trajectory.Select(point => new UtilityPathPoint
                {
                    tick = point.t,
                    x = point.p.x,
                    y = point.p.y,
                    z = point.p.z,
                })
                .ToList(),
        };
    }

    // The plugin measures flight in seconds and the API stores milliseconds. A
    // missed conversion here is silently wrong rather than an error, which is
    // why the arithmetic lives in one named place.
    public static int MillisecondsFromSeconds(float seconds)
    {
        return (int)MathF.Round(seconds * 1000f, MidpointRounding.AwayFromZero);
    }

    private static string? Text(string? value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
