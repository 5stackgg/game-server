using FiveStack.Entities.PlayerManagement;
using Xunit;

public class PlayerManagementSettingsTests
{
    private const string ServerId = "11111111-1111-1111-1111-111111111111";

    private static Func<string, string?> Env(Dictionary<string, string> values)
    {
        return name => values.TryGetValue(name, out string? value) ? value : null;
    }

    [Fact]
    public void TheNodesEnvironmentWinsOverTheFile()
    {
        PlayerManagementSettings file = new()
        {
            API_DOMAIN = "https://api.example.com",
            SERVER_ID = "file-id",
            SERVER_API_PASSWORD = "file-password",
        };

        PlayerManagementSettings resolved = file.Resolve(
            Env(
                new()
                {
                    ["API_DOMAIN"] = "https://api.5stack.test",
                    ["SERVER_ID"] = ServerId,
                    ["SERVER_API_PASSWORD"] = "env-password",
                }
            )
        );

        Assert.Equal("https://api.5stack.test", resolved.API_DOMAIN);
        Assert.Equal(ServerId, resolved.SERVER_ID);
        Assert.Equal("env-password", resolved.SERVER_API_PASSWORD);
    }

    [Fact]
    public void ABlankVariableDoesNotEraseTheFile()
    {
        PlayerManagementSettings file = new() { SERVER_ID = ServerId };

        PlayerManagementSettings resolved = file.Resolve(Env(new() { ["SERVER_ID"] = " " }));

        Assert.Equal(ServerId, resolved.SERVER_ID);
    }

    [Fact]
    public void TheApiDomainLosesATrailingSlashAndADoubledScheme()
    {
        PlayerManagementSettings file = new() { API_DOMAIN = "https://https://api.example.com/" };

        Assert.Equal("https://api.example.com", file.Resolve(Env(new())).API_DOMAIN);
    }

    [Fact]
    public void ItIsConnectedOnlyWithAServerUuidAndAPassword()
    {
        Assert.False(new PlayerManagementSettings().IsConnected());
        Assert.False(
            new PlayerManagementSettings
            {
                SERVER_ID = "not-a-uuid",
                SERVER_API_PASSWORD = "password",
            }.IsConnected()
        );
        Assert.False(new PlayerManagementSettings { SERVER_ID = ServerId }.IsConnected());
        Assert.True(
            new PlayerManagementSettings
            {
                SERVER_ID = ServerId,
                SERVER_API_PASSWORD = "password",
            }.IsConnected()
        );
    }

    [Fact]
    public void TheSyncUrlIsTheServersSanctionsRoute()
    {
        PlayerManagementSettings settings = new()
        {
            API_DOMAIN = "https://api.example.com",
            SERVER_ID = ServerId,
        };

        Assert.Equal($"https://api.example.com/sanctions/server/{ServerId}", settings.SyncUrl());
    }
}
