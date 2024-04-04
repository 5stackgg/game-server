using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class MatchCoachSystem
{
    private readonly ILogger<MatchCoachSystem> _logger;

    private Dictionary<CsTeam, CCSPlayerController?> _coaches = new Dictionary<
        CsTeam,
        CCSPlayerController?
    >
    {
        { CsTeam.Terrorist, null },
        { CsTeam.CounterTerrorist, null }
    };

    public MatchCoachSystem(ILogger<MatchCoachSystem> logger)
    {
        _logger = logger;
    }
}
