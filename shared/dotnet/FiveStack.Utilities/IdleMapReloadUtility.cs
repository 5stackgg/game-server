namespace FiveStack.Utilities;

// curtime is a 32-bit float and only a map load resets it, so the longer a map
// stays up the coarser the clock gets: ~2 ms steps past 4.5 hours and a whole
// 64-tick tick past 36, played as sluggish animation, movement and lag
// compensation. Nobody notices on an empty server, so that is when it reloads.
public static class IdleMapReloadUtility
{
    public const float MaxCurTime = 60 * 60;

    public static bool ShouldReload(float curTime, int players, bool hasMatch)
    {
        return !hasMatch && players == 0 && curTime > MaxCurTime;
    }

    public static string ReloadCommand(string mapName, string? workshopId)
    {
        return string.IsNullOrEmpty(workshopId)
            ? $"changelevel \"{mapName}\""
            : $"host_workshop_map {workshopId}";
    }
}
