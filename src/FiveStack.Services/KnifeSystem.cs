using System.Text.RegularExpressions;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class KnifeSystem
{
    private FiveStackMatch? _match;
    private readonly GameEvents _gameEvents;
    private readonly GameServer _gameServer;
    private readonly ILogger<KnifeSystem> _logger;

    private CsTeam? _winningTeam;

    public KnifeSystem(ILogger<KnifeSystem> logger, GameServer gameServer, GameEvents gameEvents)
    {
        _logger = logger;
        _gameEvents = gameEvents;
        _gameServer = gameServer;
    }

    public void Setup(FiveStackMatch match)
    {
        _match = match;
    }

    public void SetWinningTeam(CsTeam team)
    {
        _winningTeam = team;
    }

    public void Switch()
    {
        if (_match == null)
        {
            return;
        }

        _gameServer.SendCommands(new[] { "mp_swapteams" });
        _gameEvents.PublishGameEvent(_match.id, "switch", new Dictionary<string, object>());
    }

    public CsTeam? GetWinningTeam()
    {
        return _winningTeam;
    }
}
