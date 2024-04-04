using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class CoachSystem
{
    private readonly ILogger<CoachSystem> _logger;

    private Dictionary<CsTeam, CCSPlayerController?> _coaches = new Dictionary<
        CsTeam,
        CCSPlayerController?
    >
    {
        { CsTeam.Terrorist, null },
        { CsTeam.CounterTerrorist, null }
    };

    public CoachSystem(ILogger<CoachSystem> logger)
    {
        _logger = logger;
    }
}
