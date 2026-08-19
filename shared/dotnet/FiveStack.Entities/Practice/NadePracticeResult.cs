namespace FiveStack.Entities.Practice;

// The body of POST /nades/practice-result, exactly as the API defines it.
//
// success is advisory and the server recomputes the distance from the lineup it
// owns, so this is only ever filled in from a radius the API itself has already
// handed back -- a guessed radius here reports a hit the panel then logs as a
// lie.
public class NadePracticeResultPayload
{
    public string? server_id { get; set; }
    public string? session_id { get; set; }
    public string? nade_lineup_id { get; set; }
    public string? steam_id { get; set; }

    public float? land_x { get; set; }
    public float? land_y { get; set; }
    public float? land_z { get; set; }

    public bool? success { get; set; }

    public static NadePracticeResultPayload For(
        string? serverId,
        Guid sessionId,
        string lineupId,
        ulong steamId,
        Vec3 landing,
        bool? success
    )
    {
        return new NadePracticeResultPayload
        {
            server_id = string.IsNullOrEmpty(serverId) ? null : serverId,
            session_id = sessionId == Guid.Empty ? null : sessionId.ToString(),
            nade_lineup_id = lineupId,
            steam_id = steamId.ToString(),
            land_x = landing.x,
            land_y = landing.y,
            land_z = landing.z,
            success = success,
        };
    }
}

// What the API answers with. The radius is the panel's, not ours: a hard-coded
// one here would tell a player they missed a throw the panel counted.
public class NadePracticeResult
{
    public bool success { get; set; }
    public float distance { get; set; }
    public float radius { get; set; }
    public int attempts { get; set; }
    public int successes { get; set; }
    public int current_streak { get; set; }
    public int best_streak { get; set; }
    public DateTime? mastered_at { get; set; }
}
