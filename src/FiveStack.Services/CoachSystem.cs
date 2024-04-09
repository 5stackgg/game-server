using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class CoachSystem {

    private readonly MatchEvents _gameEvents;
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly ILogger<CoachSystem> _logger;

    public Dictionary<CsTeam, CCSPlayerController?> _coaches = new Dictionary<
        CsTeam,
        CCSPlayerController?
    >
    {
        { CsTeam.Terrorist, null },
        { CsTeam.CounterTerrorist, null }
    };

    public CoachSystem(
        ILogger<CoachSystem> logger,
        MatchEvents gameEvents,
        GameServer gameServer,
        MatchService matchService
    )
    {
        _logger = logger;
        _gameEvents = gameEvents;
        _gameServer = gameServer;
        _matchService = matchService;
    }

  
    public bool IsCoach(CCSPlayerController player, CsTeam team)
    {
        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();
        if (matchData != null && matchData.coaches)
        {
            if(player.SteamID.ToString() == matchData.lineup_1.coach_steam_id || player.SteamID.ToString() == matchData.lineup_1.coach_steam_id) {
                return true;
            }
        }

        return true;   
    }
}
