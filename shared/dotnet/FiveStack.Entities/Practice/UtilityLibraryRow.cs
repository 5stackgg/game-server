using FiveStack.Utilities;

namespace FiveStack.Entities.Practice;

// A row of GET /utility/library, exactly as the API returns it. The steps of a
// playbook inline the same shape.
//
// There is no flight path here. A row carries enough to stand a player on the
// lineup, point them at it and re-emit the throw exactly, but a preview of the
// line itself needs a second call to GET /utility/{id}/trajectory.
public class UtilityLibraryRow
{
    public string? id { get; set; }
    public string? name { get; set; }
    public string? map_name { get; set; }
    public string? utility_type { get; set; }
    public string? side { get; set; }
    public string? technique { get; set; }
    public string? throw_strength { get; set; }
    public bool? jump_throw_bind { get; set; }

    public double? aim_tolerance { get; set; }

    public float? origin_x { get; set; }
    public float? origin_y { get; set; }
    public float? origin_z { get; set; }
    public float? eye_z { get; set; }

    public float? view_yaw { get; set; }
    public float? view_pitch { get; set; }

    public float? land_x { get; set; }
    public float? land_y { get; set; }
    public float? land_z { get; set; }

    // The engine's own physics seed. Null by design: a lineup mined from a
    // demo, authored in the editor or imported was never watched by a plugin
    // and has no seed to give.
    public float? initial_pos_x { get; set; }
    public float? initial_pos_y { get; set; }
    public float? initial_pos_z { get; set; }
    public float? initial_vel_x { get; set; }
    public float? initial_vel_y { get; set; }
    public float? initial_vel_z { get; set; }

    public int? flight_time_ms { get; set; }
    public string? visibility { get; set; }
    public string? confidence { get; set; }
    public string? author_steam_id { get; set; }

    public LineupRecord ToLineup()
    {
        float originX = origin_x ?? 0f;
        float originY = origin_y ?? 0f;

        var lineup = new LineupRecord
        {
            id = id,
            // The panel's id is the only stable identity a fetched lineup has,
            // so it doubles as the local one and .delete keeps working after a
            // reload.
            client_id = id ?? Guid.NewGuid().ToString(),
            name = name ?? "",
            map = map_name ?? "",
            utility_type = PracticeLineupUtility.NormalizeUtilityType(utility_type ?? ""),
            side = side ?? "TERRORIST",
            technique = technique ?? "",
            strength = throw_strength,
            aim_tolerance = (float)(aim_tolerance ?? 0d),
            visibility = visibility ?? "Private",
            confidence = confidence,
            author_steam_id = author_steam_id ?? "",

            release = new ThrowSnapshot
            {
                feet_position = new Vec3(originX, originY, origin_z ?? 0f),
                eye_position = new Vec3(originX, originY, eye_z ?? 0f),
                yaw = view_yaw ?? 0f,
                pitch = view_pitch ?? 0f,
                jump_throw = jump_throw_bind ?? false,
            },

            detonation_position = new Vec3(land_x ?? 0f, land_y ?? 0f, land_z ?? 0f),
            flight_time = (flight_time_ms ?? 0) / 1000f,
        };

        // A seed is one thing, not six numbers, so it is taken whole or not at
        // all. Everything downstream reads a zero velocity as "there is no
        // seed", and a null quietly widened into a zero is what would launch a
        // replayed grenade out of the world origin instead of refusing to fire.
        if (HasSeed())
        {
            lineup.initial_position = new Vec3(
                initial_pos_x!.Value,
                initial_pos_y!.Value,
                initial_pos_z!.Value
            );
            lineup.initial_velocity = new Vec3(
                initial_vel_x!.Value,
                initial_vel_y!.Value,
                initial_vel_z!.Value
            );
        }

        return lineup;
    }

    // A thrown grenade never leaves the hand at rest, so a stored velocity of
    // zero is a column that was never filled in rather than a throw that stood
    // still.
    public bool HasSeed()
    {
        if (
            initial_pos_x == null
            || initial_pos_y == null
            || initial_pos_z == null
            || initial_vel_x == null
            || initial_vel_y == null
            || initial_vel_z == null
        )
        {
            return false;
        }

        return new Vec3(
                initial_vel_x.Value,
                initial_vel_y.Value,
                initial_vel_z.Value
            ).Length() > 0f;
    }
}
