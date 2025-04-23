using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace FiveStack;

public class KnifeSystem
{
    private readonly GameServer _gameServer;
    private readonly MatchEvents _matchEvents;
    private readonly MatchService _matchService;
    private readonly ILogger<KnifeSystem> _logger;

    private Timer? _knifeRoundTimer;

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
        _gameServer.SendCommands(new[] { "exec 5stack.knife.cfg" });
        _gameServer.SendCommands(new[] { "mp_restartgame 1" });

        await Task.Delay(3000);

        Server.NextFrame(() =>
        {
            _gameServer.Message(HudDestination.Alert, "KNIFE KNIFE KNIFE!");
        });
    }

    public void SetWinningTeam(CsTeam team)
    {
        _gameServer.SendCommands(new[] { "mp_pause_match" });
        _winningTeam = team;

        _knifeRoundTimer = TimerUtility.AddTimer(3, SetupKnifeMessage, TimerFlags.REPEAT);

        SetupKnifeMessage();

        _gameServer.Message(
            HudDestination.Alert,
            $"{(team == CsTeam.Terrorist ? "Terrorist" : "CT")} - Captain is Picking Sides!"
        );
    }

    public void SetupKnifeMessage()
    {
        if (_winningTeam == null)
        {
            return;
        }

        CCSPlayerController? captain = _matchService
            ?.GetCurrentMatch()
            ?.captainSystem?.GetTeamCaptain(_winningTeam.Value);

        if (captain == null)
        {
            _logger.LogCritical("missing team captain");
            return;
        }

        captain.PrintToCenter(
            $"As the captain you must select to {ChatColors.Green}{CommandUtility.PublicChatTrigger}stay {ChatColors.Default} or {ChatColors.Green}{CommandUtility.PublicChatTrigger}switch"
        );
    }

    public void Stay(CCSPlayerController player)
    {
        _logger.LogInformation("Knife round staying");

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

        ResetKnifeRound();

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

        ResetKnifeRound();

        _gameServer.Message(
            HudDestination.Alert,
            $"captain picked to {ChatColors.Red}swap {ChatColors.Default}sides"
        );

        _matchEvents.PublishGameEvent("switch", new Dictionary<string, object>());
    }

    public void Skip()
    {
        ResetKnifeRound();

        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null || !match.IsKnife())
        {
            return;
        }

        _gameServer.Message(HudDestination.Center, $"Skipping Knife.");

        match.UpdateMapStatus(eMapStatus.Live);
    }

    public void ConfirmSwitch()
    {
        _logger.LogInformation("Knife round confirming switch");
        _matchService.GetMatchFromApi(eMapStatus.Live);
    }

    public CsTeam? GetWinningTeam()
    {
        return _winningTeam;
    }

    public void ResetKnifeRound()
    {
        _knifeRoundTimer?.Kill();
        _knifeRoundTimer = null;
        _winningTeam = null;
    }
}
