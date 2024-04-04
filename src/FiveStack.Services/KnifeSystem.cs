using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class KnifeSystem
{
    private readonly MatchEvents _gameEvents;
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly ILogger<KnifeSystem> _logger;

    private CsTeam? _winningTeam;

    public KnifeSystem(
        ILogger<KnifeSystem> logger,
        GameServer gameServer,
        MatchEvents gameEvents,
        MatchService matchService
    )
    {
        _logger = logger;
        _matchService = matchService;
        _gameEvents = gameEvents;
        _gameServer = gameServer;
    }

    public void SetWinningTeam(CsTeam team)
    {
        _winningTeam = team;
    }

    public void Switch()
    {
        Guid matchId = _matchService.GetCurrentMatch()?.GetMatchData()?.id ?? Guid.Empty;
        if (matchId == Guid.Empty)
        {
            return;
        }

        _gameServer.SendCommands(new[] { "mp_swapteams" });
        _gameEvents.PublishGameEvent(matchId, "switch", new Dictionary<string, object>());
    }

    public CsTeam? GetWinningTeam()
    {
        return _winningTeam;
    }
}
