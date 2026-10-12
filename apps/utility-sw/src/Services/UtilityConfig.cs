using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace UtilityPractice;

// Configuration arrives as addons/swiftlys2/configs/utility-practice.json,
// written by the panel from the registry's wiring block. The url and api key are
// provisioned per install, exactly as the inventory plugin receives its own.
public class UtilityConfig
{
    public string UtilityUrl { get; private set; } = "";

    // The plugin key alone buys nothing: every utility endpoint also wants the
    // server to prove which server it is.
    public string ServerId { get; private set; } = "";
    public string ServerApiPassword { get; private set; } = "";
    public bool ReplayEnabled { get; private set; } = true;
    public bool InfiniteUtility { get; private set; } = true;
    public bool NoFlash { get; private set; } = true;
    public bool GhostPreview { get; private set; } = true;
    public bool GhostProjectile { get; private set; } = false;
    public int MaxSaved { get; private set; } = 200;

    // Costs nothing where the Panorama addon is not mounted: the layout
    // fails to spawn and every panel falls back to centre text.
    public bool HudEnabled { get; private set; } = true;

    // A server booked to film lineup previews. The plugin directs the shot and
    // draws, prints and tints nothing a camera would pick up.
    public bool RenderMode { get; private set; }

    private readonly ILogger<UtilityConfig> _logger;

    public UtilityConfig(ILogger<UtilityConfig> logger)
    {
        _logger = logger;
    }

    private class ConfigFile
    {
        public string? utility_url { get; set; }
        public string? server_id { get; set; }
        public string? server_api_password { get; set; }
        public bool? np_replay_enabled { get; set; }
        public bool? np_infinite_utility { get; set; }
        public bool? np_no_flash { get; set; }
        public bool? np_ghost_preview { get; set; }
        public bool? np_ghost_projectile { get; set; }
        public int? np_max_saved { get; set; }
        public bool? np_hud_enabled { get; set; }
    }

    // Candidates rather than one path: the registry writes
    // addons/swiftlys2/configs/utility-practice.json, but an operator may drop
    // the file beside the plugin instead.
    public void Load(params string[] configDirectories)
    {
        // Env wins over the file so an operator can override a provisioned key
        // without editing a file the panel rewrites.
        UtilityUrl = Environment.GetEnvironmentVariable("UTILITY_URL") ?? "";
        ServerId = Environment.GetEnvironmentVariable("SERVER_ID") ?? "";
        ServerApiPassword = Environment.GetEnvironmentVariable("SERVER_API_PASSWORD") ?? "";

        string? path = configDirectories
            .Where(directory => !string.IsNullOrEmpty(directory))
            .Select(directory => Path.Join(directory, "utility-practice.json"))
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
                    if (string.IsNullOrEmpty(UtilityUrl))
                    {
                        UtilityUrl = parsed.utility_url ?? "";
                    }
                    if (string.IsNullOrEmpty(ServerId))
                    {
                        ServerId = parsed.server_id ?? "";
                    }
                    if (string.IsNullOrEmpty(ServerApiPassword))
                    {
                        ServerApiPassword = parsed.server_api_password ?? "";
                    }
                    ReplayEnabled = parsed.np_replay_enabled ?? ReplayEnabled;
                    InfiniteUtility = parsed.np_infinite_utility ?? InfiniteUtility;
                    NoFlash = parsed.np_no_flash ?? NoFlash;
                    GhostPreview = parsed.np_ghost_preview ?? GhostPreview;
                    GhostProjectile = parsed.np_ghost_projectile ?? GhostProjectile;
                    MaxSaved = parsed.np_max_saved ?? MaxSaved;
                    HudEnabled = parsed.np_hud_enabled ?? HudEnabled;
                }
            }
            catch (Exception error)
            {
                _logger.LogError(error, "unable to read {path}", path);
            }
        }

        // Operator switches: nothing provisions NP_GHOST_PROJECTILE or
        // NP_GHOST_PREVIEW, and a render server turns both off below. Env wins
        // over the file, same as the url/keys above.
        string? ghostProjectile =
            Environment.GetEnvironmentVariable("NP_GHOST_PROJECTILE");
        if (!string.IsNullOrEmpty(ghostProjectile))
        {
            GhostProjectile = ghostProjectile == "1" || ghostProjectile == "true";
        }
        string? ghostPreview =
            Environment.GetEnvironmentVariable("NP_GHOST_PREVIEW");
        if (!string.IsNullOrEmpty(ghostPreview))
        {
            GhostPreview = ghostPreview == "1" || ghostPreview == "true";
        }

        string? renderMode = Environment.GetEnvironmentVariable("UTILITY_RENDER_MODE");
        RenderMode = renderMode == "1" || renderMode == "true";

        if (RenderMode)
        {
            GhostProjectile = false;
            GhostPreview = false;
            HudEnabled = false;
        }

        UtilityUrl = UtilityUrl.TrimEnd('/');

        // A doubled scheme dials a host literally named "https" and then dies
        // quietly on DNS -- the exact failure is invisible from outside the
        // pod, so it is collapsed here and the resolved URL is said out loud.
        UtilityUrl = System.Text.RegularExpressions.Regex.Replace(
            UtilityUrl,
            "^(https?://)+",
            "$1"
        );

        if (!string.IsNullOrEmpty(UtilityUrl))
        {
            _logger.LogInformation("utility practice panel: {url}", UtilityUrl);
        }

        if (string.IsNullOrEmpty(UtilityUrl) || string.IsNullOrEmpty(ServerApiPassword))
        {
            // Not fatal: local practice commands still work, saves just cannot
            // reach the panel. Saying so once at load beats a silent failure on
            // the player's first .save.
            _logger.LogWarning(
                "utility practice is not connected to a panel; lineups cannot be saved or loaded"
            );
        }
    }

    public bool IsConnected()
    {
        return !string.IsNullOrEmpty(UtilityUrl) && !string.IsNullOrEmpty(ServerApiPassword);
    }
}
