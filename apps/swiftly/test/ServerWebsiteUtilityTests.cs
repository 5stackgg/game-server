using FiveStack.Utilities;
using Xunit;

public class ServerWebsiteUtilityTests
{
    [Theory]
    [InlineData("https://api.5stack.gg", "https://5stack.gg")]
    [InlineData("https://api.cs.adria.gg", "https://cs.adria.gg")]
    [InlineData("https://api.cs.adria.gg/", "https://cs.adria.gg")]
    [InlineData("https://api.panel.example:8443", "https://panel.example:8443")]
    public void FromApiDomain_DropsTheApiSubdomain(string apiDomain, string website)
    {
        Assert.Equal(website, ServerWebsiteUtility.FromApiDomain(apiDomain));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("api.cs.adria.gg")]
    [InlineData("http://api.cs.adria.gg")]
    [InlineData("https://cs.adria.gg")]
    [InlineData("https://localhost:5585")]
    public void FromApiDomain_FallsBackTo5stack(string? apiDomain)
    {
        Assert.Equal(ServerWebsiteUtility.Default, ServerWebsiteUtility.FromApiDomain(apiDomain));
    }
}
