using CounterStrikeSharp.API.Core;
using FiveStack.Entities.PlayerManagement;

namespace PlayerManagement;

// CounterStrikeSharp writes this to
// addons/counterstrikesharp/configs/plugins/PlayerManagement/PlayerManagement.json
// on first load, which is where a server outside a 5stack node sets it.
public class PlayerManagementConfig : IBasePluginConfig
{
    public int Version { get; set; } = 1;
    public string API_DOMAIN { get; set; } = "https://api.5stack.gg";
    public string SERVER_ID { get; set; } = "";
    public string SERVER_API_PASSWORD { get; set; } = "";

    public PlayerManagementSettings Settings()
    {
        return new PlayerManagementSettings
        {
            API_DOMAIN = API_DOMAIN,
            SERVER_ID = SERVER_ID,
            SERVER_API_PASSWORD = SERVER_API_PASSWORD,
        }.Resolve(Environment.GetEnvironmentVariable);
    }
}
