using CounterStrikeSharp.API.Core;

public class FiveStackConfig : IBasePluginConfig
{
    public int Version { get; set; } = 1;
    public string WS_DOMAIN { get; set; } = "wss://ws.5stack.gg";
    public string API_DOMAIN { get; set; } = "https://api.5stack.gg";
    public string DEMOS_DOMAIN { get; set; } = "https://demos.5stack.gg";
    public string SERVER_ID { get; set; } = "";
    public string SERVER_API_PASSWORD { get; set; } = "";
}
