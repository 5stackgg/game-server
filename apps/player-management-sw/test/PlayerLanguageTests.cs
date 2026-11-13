using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

// SwiftlyS2 maps a latam client to es-419 and a portuguese one to pt, loads only a
// file with exactly that name, and falls back to English rather than the server's
// language when there is none.
public class PlayerLanguageTests
{
    private static string SrcDir([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "src"));
    }

    [Theory]
    [InlineData("es-419", "es")]
    [InlineData("pt", "pt-BR")]
    public void AClientOnAVariantOfALanguageWeShipGetsThatLanguage(string language, string shipped)
    {
        XElement[] aliases = XDocument
            .Load(Path.Combine(SrcDir(), "PlayerManagement.csproj"))
            .Descendants("Content")
            .Where(content =>
                (string?)content.Attribute("Link") == $"resources/translations/{language}.jsonc"
            )
            .ToArray();

        Assert.Single(aliases);
        Assert.EndsWith(
            $"resources/translations/{shipped}.jsonc",
            (string)aliases[0].Attribute("Include")!
        );
        Assert.True(
            File.Exists(Path.Combine(SrcDir(), "resources", "translations", $"{shipped}.jsonc"))
        );
    }

    [Fact]
    public void APlayerGetsTheirLanguageWhenItShipsAndOtherwiseTheServers()
    {
        string plugin = Regex.Replace(
            Regex.Replace(
                File.ReadAllText(Path.Combine(SrcDir(), "PlayerManagementPlugin.cs")),
                @"//[^\n]*",
                ""
            ),
            @"\s+",
            ""
        );

        Assert.Single(Regex.Matches(plugin, @"GetPlayerLocalizer\("));
        Assert.Contains(
            "privateILocalizerLocalizerFor(IPlayerplayer){if(!TranslationUtility.HasTranslation(Core.PluginPath,player.PlayerLanguage.Value)){returnCore.Localizer;}returnCore.Translation.GetPlayerLocalizer(player);}",
            plugin
        );
    }
}
