using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NadePractice;

// Configuration arrives as addons/counterstrikesharp/configs/nade-practice.json,
// written by the panel from the registry's wiring block. The url and api key are
// provisioned per install, exactly as the inventory plugin receives its own.
public class NadesConfig
{
    public string NadesUrl { get; private set; } = "";
    public string NadesApiKey { get; private set; } = "";

    // The plugin key alone buys nothing: every nade endpoint also wants the
    // server to prove which server it is.
    public string ServerId { get; private set; } = "";
    public string ServerApiPassword { get; private set; } = "";
    public bool RecordEnabled { get; private set; } = true;
    public bool ReplayEnabled { get; private set; } = true;
    public bool InfiniteNades { get; private set; } = true;
    public bool NoFlash { get; private set; } = true;
    public bool GhostPreview { get; private set; } = true;
    public int MaxSaved { get; private set; } = 200;

    private readonly ILogger<NadesConfig> _logger;

    public NadesConfig(ILogger<NadesConfig> logger)
    {
        _logger = logger;
    }

    private class ConfigFile
    {
        public string? nades_url { get; set; }
        public string? nades_apikey { get; set; }
        public string? server_id { get; set; }
        public string? server_api_password { get; set; }
        public bool? np_record_enabled { get; set; }
        public bool? np_replay_enabled { get; set; }
        public bool? np_infinite_nades { get; set; }
        public bool? np_no_flash { get; set; }
        public bool? np_ghost_preview { get; set; }
        public int? np_max_saved { get; set; }
    }

    // Candidates rather than one path: the registry writes
    // addons/{runtime}/configs/nade-practice.json, but the two runtimes root
    // their plugin directories differently and an operator may drop the file
    // beside the plugin instead.
    public void Load(params string[] configDirectories)
    {
        // Env wins over the file so an operator can override a provisioned key
        // without editing a file the panel rewrites.
        NadesUrl = Environment.GetEnvironmentVariable("NADES_URL") ?? "";
        NadesApiKey = Environment.GetEnvironmentVariable("NADES_API_KEY") ?? "";
        ServerId = Environment.GetEnvironmentVariable("SERVER_ID") ?? "";
        ServerApiPassword = Environment.GetEnvironmentVariable("SERVER_API_PASSWORD") ?? "";

        string? path = configDirectories
            .Where(directory => !string.IsNullOrEmpty(directory))
            .Select(directory => Path.Join(directory, "nade-practice.json"))
            .FirstOrDefault(File.Exists);

        if (path != null)
        {
            try
            {
                ConfigFile? parsed = JsonSerializer.Deserialize<ConfigFile>(
                    File.ReadAllText(path)
                );

                if (parsed != null)
                {
                    if (string.IsNullOrEmpty(NadesUrl))
                    {
                        NadesUrl = parsed.nades_url ?? "";
                    }
                    if (string.IsNullOrEmpty(NadesApiKey))
                    {
                        NadesApiKey = parsed.nades_apikey ?? "";
                    }
                    if (string.IsNullOrEmpty(ServerId))
                    {
                        ServerId = parsed.server_id ?? "";
                    }
                    if (string.IsNullOrEmpty(ServerApiPassword))
                    {
                        ServerApiPassword = parsed.server_api_password ?? "";
                    }
                    RecordEnabled = parsed.np_record_enabled ?? RecordEnabled;
                    ReplayEnabled = parsed.np_replay_enabled ?? ReplayEnabled;
                    InfiniteNades = parsed.np_infinite_nades ?? InfiniteNades;
                    NoFlash = parsed.np_no_flash ?? NoFlash;
                    GhostPreview = parsed.np_ghost_preview ?? GhostPreview;
                    MaxSaved = parsed.np_max_saved ?? MaxSaved;
                }
            }
            catch (Exception error)
            {
                _logger.LogError(error, "unable to read {path}", path);
            }
        }

        NadesUrl = NadesUrl.TrimEnd('/');

        if (string.IsNullOrEmpty(NadesUrl) || string.IsNullOrEmpty(NadesApiKey))
        {
            // Not fatal: local practice commands still work, saves just cannot
            // reach the panel. Saying so once at load beats a silent failure on
            // the player's first .save.
            _logger.LogWarning(
                "nade practice is not connected to a panel; lineups cannot be saved or loaded"
            );
        }
    }

    public bool IsConnected()
    {
        return !string.IsNullOrEmpty(NadesUrl) && !string.IsNullOrEmpty(NadesApiKey);
    }
}
