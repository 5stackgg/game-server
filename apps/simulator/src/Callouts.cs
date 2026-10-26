namespace FiveStack.Simulator;

public static class Callouts
{
    private static readonly Dictionary<string, string[]> ByMap = new()
    {
        ["de_dust2"] =
        [
            "TSpawn",
            "OutsideLong",
            "LongDoors",
            "LongA",
            "BombsiteA",
            "ARamp",
            "ShortStairs",
            "Catwalk",
            "Middle",
            "MidDoors",
            "TopofMid",
            "CTSpawn",
            "OutsideTunnels",
            "UpperTunnels",
            "LowerTunnels",
            "BDoors",
            "BombsiteB",
            "BWindow",
            "Pit",
        ],
        ["de_mirage"] =
        [
            "TSpawn",
            "TRamp",
            "Palace",
            "BombsiteA",
            "Jungle",
            "Connector",
            "Stairs",
            "Tetris",
            "Middle",
            "TopofMid",
            "Catwalk",
            "Shop",
            "Apartments",
            "Underpass",
            "BombsiteB",
            "Bench",
            "Market",
            "CTSpawn",
            "Ticket",
        ],
        ["de_inferno"] =
        [
            "TSpawn",
            "TRamp",
            "Banana",
            "BombsiteB",
            "CTSpawn",
            "Arch",
            "Library",
            "BombsiteA",
            "Pit",
            "Apartments",
            "Balcony",
            "Middle",
            "SecondMid",
            "Graveyard",
            "Construction",
            "Quad",
            "Boiler",
        ],
        ["de_nuke"] =
        [
            "TSpawn",
            "Outside",
            "Silo",
            "Lobby",
            "Hut",
            "Ramp",
            "BombsiteA",
            "Heaven",
            "Hell",
            "BombsiteB",
            "Vents",
            "Secret",
            "CTSpawn",
            "Squeaky",
            "Garage",
        ],
        ["de_ancient"] =
        [
            "TSpawn",
            "Outside",
            "Ruins",
            "BombsiteA",
            "Donut",
            "Middle",
            "Cave",
            "BombsiteB",
            "Ramp",
            "CTSpawn",
            "Temple",
            "Alley",
        ],
        ["de_anubis"] =
        [
            "TSpawn",
            "Street",
            "Bridge",
            "BombsiteA",
            "Connector",
            "Middle",
            "Canal",
            "BombsiteB",
            "Palace",
            "CTSpawn",
            "Walkway",
        ],
    };

    private static readonly string[] Generic =
    [
        "TSpawn",
        "CTSpawn",
        "BombsiteA",
        "BombsiteB",
        "Middle",
        "Connector",
        "Long",
        "Short",
    ];

    public static string Pick(string map, Random random)
    {
        string[] places = ByMap.TryGetValue(map, out string[]? known) ? known : Generic;

        return places[random.Next(places.Length)];
    }
}
