using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

public class CoachSystem
{
    private readonly MatchService _matchService;
    private readonly ILogger<CoachSystem> _logger;

    public Dictionary<Team, IPlayer?> _coaches = new Dictionary<
        Team,
        IPlayer?
    >
    {
        { Team.T, null },
        { Team.CT, null },
    };

    public CoachSystem(ILogger<CoachSystem> logger, MatchService matchService)
    {
        _logger = logger;
        _matchService = matchService;
    }

    public bool IsCoach(IPlayer player, Team team)
    {
        if (player.IsFakeClient || !player.IsValid)
        {
            return false;
        }

        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();
        if (matchData != null && matchData.options.coaches)
        {
            if (
                player?.SteamID.ToString() == matchData.lineup_1.coach_steam_id
                || player?.SteamID.ToString() == matchData.lineup_2.coach_steam_id
            )
            {
                return true;
            }
        }

        return false;
    }
}
