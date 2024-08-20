using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class CoachSystem
{
    private readonly MatchService _matchService;
    private readonly ILogger<CoachSystem> _logger;

    public Dictionary<CsTeam, CCSPlayerController?> _coaches = new Dictionary<
        CsTeam,
        CCSPlayerController?
    >
    {
        { CsTeam.Terrorist, null },
        { CsTeam.CounterTerrorist, null },
    };

    public CoachSystem(ILogger<CoachSystem> logger, MatchService matchService)
    {
        _logger = logger;
        _matchService = matchService;
    }

    public bool IsCoach(CCSPlayerController player, CsTeam team)
    {
        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();
        if (matchData != null && matchData.options.coaches)
        {
            if (
                player.SteamID.ToString() == matchData.lineup_1.coach_steam_id
                || player.SteamID.ToString() == matchData.lineup_1.coach_steam_id
            )
            {
                return true;
            }
        }

        return true;
    }
}
