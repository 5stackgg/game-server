namespace FiveStack.Utilities;

// SwiftlyS2 loads <language>.jsonc from here and falls back to English, not the
// server's language, when a player's language has no file.
public static class TranslationUtility
{
    public static bool HasTranslation(string pluginPath, string language)
    {
        return File.Exists(Path.Join(pluginPath, "resources", "translations", $"{language}.jsonc"));
    }
}
