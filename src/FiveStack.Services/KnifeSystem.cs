using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Enums;
using FiveStack.Utilities;
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

    public async void Start()
    {
        _gameServer.SendCommands(new[] { "mp_warmup_end" });
        _gameServer.SendCommands(new[] { "exec knife" });
        _gameServer.SendCommands(new[] { "mp_restartgame 1" });

        await Task.Delay(3000);

        Server.NextFrame(() =>
        {
            _gameServer.Message(HudDestination.Alert, "KNIFE KNIFE KNIFE!");
        });
    }

    public void SetWinningTeam(CsTeam team)
    {
        _gameServer.SendCommands(new[] { "mp_warmup_start" });
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
            $"As the captain you must select to {ChatColors.Green}{CommandUtility.PublicChatTrigger}stay {ChatColors.Default} or {ChatColors.Green}{CommandUtility.PublicChatTrigger}switch",
            captain
        );
        _gameServer.Message(
            HudDestination.Alert,
            $"{(team == CsTeam.Terrorist ? "Terrorist" : "CT")} - Captain is Picking Sides!"
        );
    }

    public void Stay(CCSPlayerController player)
    {
        CsTeam winningTeam = GetWinningTeam() ?? CsTeam.None;
        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null || winningTeam == CsTeam.None || !match.IsKnife())
        {
            return;
        }

        if (match.captainSystem.IsCaptain(player, winningTeam) == false)
        {
            _gameServer.Message(
                HudDestination.Chat,
                $" {ChatColors.Red}You are not the captain!",
                player
            );
            return;
        }

        _gameServer.Message(
            HudDestination.Alert,
            $"captain picked to {ChatColors.Red}stay {ChatColors.Default}sides"
        );

        _gameServer.SendCommands(new[] { "mp_restartgame 1" });

        match.UpdateMapStatus(eMapStatus.Live);
    }

    public void Switch(CCSPlayerController player)
    {
        CsTeam winningTeam = GetWinningTeam() ?? CsTeam.None;
        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null || winningTeam == CsTeam.None || !match.IsKnife())
        {
            return;
        }

        _logger.LogInformation($"Knife round winning team: {winningTeam}");

        if (match.captainSystem.IsCaptain(player, winningTeam) == false)
        {
            _gameServer.Message(
                HudDestination.Chat,
                $" {ChatColors.Red}You are not the captain!",
                player
            );
            return;
        }

        _gameServer.Message(
            HudDestination.Alert,
            $"captain picked to {ChatColors.Red}swap {ChatColors.Default}sides"
        );

        _gameServer.SendCommands(new[] { "mp_swapteams", "mp_restartgame 1" });

        _matchEvents.PublishGameEvent("switch", new Dictionary<string, object>());

        match.UpdateMapStatus(eMapStatus.Live);
    }

    public void Skip()
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null || !match.IsKnife())
        {
            return;
        }

        _gameServer.Message(HudDestination.Center, $"Skipping Knife.");

        match.UpdateMapStatus(eMapStatus.Live);
    }

    public CsTeam? GetWinningTeam()
    {
        return _winningTeam;
    }
}
