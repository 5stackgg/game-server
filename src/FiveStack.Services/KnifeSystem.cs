using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class KnifeSystem
{
    private readonly GameServer _gameServer;
    private readonly MatchEvents _matchEvents;
    private readonly MatchService _matchService;
    private readonly ILogger<KnifeSystem> _logger;

    private CsTeam? _winningTeam;

    public KnifeSystem(
        ILogger<KnifeSystem> logger,
        GameServer gameServer,
        MatchEvents matchEvents,
        MatchService matchService
    )
    {
        _logger = logger;
        _matchService = matchService;
        _matchEvents = matchEvents;
        _gameServer = gameServer;
    }

    public void SetWinningTeam(CsTeam team)
    {
        _winningTeam = team;

        CCSPlayerController? captain = _matchService
            ?.GetCurrentMatch()
            ?.captainSystem?.GetTeamCaptain(team);

        if (captain == null)
        {
            _logger.LogCritical("missing team captain");
            return;
        }

        _gameServer.Message(
            HudDestination.Chat,
            $"As the captain you must select to {ChatColors.Green}.stay {ChatColors.Default} or {ChatColors.Green}.switch",
            captain
        );
        _gameServer.Message(
            HudDestination.Alert,
            $"{(team == CsTeam.Terrorist ? "Terrorist" : "CT")} - Captain is Picking Sides!"
        );
    }

    public void Switch()
    {
        _gameServer.SendCommands(new[] { "mp_swapteams" });
        _matchEvents.PublishGameEvent("switch", new Dictionary<string, object>());
    }

    public CsTeam? GetWinningTeam()
    {
        return _winningTeam;
    }
}
