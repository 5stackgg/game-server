using System.Runtime.InteropServices;
using System.Text.Json;

namespace FiveStack.Utilities;

public record GamedataSignature(string Library, string Pattern);

// Signatures come from shared/gamedata/fivestack.gamedata.json, which ships
// beside the plugin. It is the file the gamedata validator checks after every
// game update, so what a plugin scans for is what was validated.
public static class GamedataUtility
{
    public static string PathIn(string pluginPath)
    {
        return Path.Join(pluginPath, "resources", "gamedata", "fivestack.gamedata.json");
    }

    public static GamedataSignature? Load(string pluginPath, string name)
    {
        return Parse(
            File.ReadAllText(PathIn(pluginPath)),
            name,
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
        );
    }

    public static GamedataSignature? Parse(string json, string name, bool linux)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        if (
            !document.RootElement.TryGetProperty(name, out JsonElement entry)
            || !entry.TryGetProperty("signatures", out JsonElement signatures)
            || !signatures.TryGetProperty("library", out JsonElement library)
            || !signatures.TryGetProperty(linux ? "linux" : "windows", out JsonElement pattern)
        )
        {
            return null;
        }

        string? libraryName = library.GetString();
        string? bytes = pattern.GetString();

        if (string.IsNullOrWhiteSpace(libraryName) || string.IsNullOrWhiteSpace(bytes))
        {
            return null;
        }

        return new GamedataSignature(libraryName, bytes);
    }
}
