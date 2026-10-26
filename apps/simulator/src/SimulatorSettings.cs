using System.Collections;
using System.Globalization;

namespace FiveStack.Simulator;

// Read from the same environment the api gives the real server's Job, plus
// the SIM_ knobs that only the simulator understands.
public sealed record SimulatorSettings
{
    public const double DefaultSpeed = 30;

    public string? ServerId { get; init; }
    public string? ApiPassword { get; init; }
    public string ApiUrl { get; init; } = "";
    public string WsUrl { get; init; } = "";
    public int Port { get; init; } = 27015;
    public string RconPassword { get; init; } = "";
    public string StartMap { get; init; } = "de_inferno";
    public bool OnGameNode { get; init; }
    public string PluginVersion { get; init; } = "dev";

    // Game seconds per real second: 1 plays at CS2's own pace.
    public double Speed { get; init; } = DefaultSpeed;
    public int? Seed { get; init; }

    public static SimulatorSettings FromEnvironment(IDictionary environment)
    {
        string? Get(string name)
        {
            string? value = environment[name] as string;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        double speed =
            double.TryParse(
                Get("SIM_SPEED"),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed
            )
            && parsed > 0
                ? parsed
                : DefaultSpeed;

        return new SimulatorSettings
        {
            ServerId = Get("SERVER_ID"),
            ApiPassword = Get("SERVER_API_PASSWORD"),
            ApiUrl = Get("API_DOMAIN") ?? "",
            WsUrl = Get("WS_DOMAIN") ?? "",
            Port = int.TryParse(Get("SERVER_PORT"), out int port) ? port : 27015,
            RconPassword = Get("RCON_PASSWORD") ?? "",
            StartMap = StartMapFrom(Get("EXTRA_GAME_PARAMS")) ?? Get("DEFAULT_MAP") ?? "de_inferno",
            OnGameNode = Get("GAME_NODE_SERVER") == "true",
            PluginVersion = Get("RELEASE_VERSION") ?? "dev",
            Speed = speed,
            Seed = int.TryParse(Get("SIM_SEED"), out int seed) ? seed : null,
        };
    }

    public static string? StartMapFrom(string? gameParams)
    {
        string[] parts = (gameParams ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int index = Array.IndexOf(parts, "+map");

        return index >= 0 && index + 1 < parts.Length ? parts[index + 1] : null;
    }
}
