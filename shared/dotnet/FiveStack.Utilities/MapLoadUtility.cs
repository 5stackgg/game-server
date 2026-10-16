using FiveStack.Entities;

namespace FiveStack.Utilities;

public static class MapLoadUtility
{
    // A workshop id only says where to fetch a map the install does not ship;
    // when the client has its own copy, that is the one a match plays.
    public static bool LoadsFromWorkshop(Map map, Func<string, bool> isMapValid)
    {
        return !string.IsNullOrEmpty(map.workshop_map_id) && !isMapValid(map.name);
    }

    public static string ChangeCommand(Map map, Func<string, bool> isMapValid)
    {
        return LoadsFromWorkshop(map, isMapValid)
            ? $"host_workshop_map {map.workshop_map_id}"
            : $"changelevel \"{map.name}\"";
    }

    public static bool IsLoaded(
        Map map,
        string currentMapName,
        string currentWorkshopId,
        Func<string, bool> isMapValid
    )
    {
        if (LoadsFromWorkshop(map, isMapValid))
        {
            return map.workshop_map_id == currentWorkshopId;
        }

        return currentMapName.ToLower().Contains(map.name.ToLower());
    }
}
