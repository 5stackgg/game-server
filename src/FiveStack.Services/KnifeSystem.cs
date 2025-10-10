using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace FiveStack;

public class KnifeSystem
{
    private readonly GameServer _gameServer;
    private readonly MatchEvents _matchEvents;
    private readonly MatchService _matchService;
    private readonly ILogger<KnifeSystem> _logger;
    private readonly EnvironmentService _environmentService;
    private readonly IStringLocalizer _localizer;
    private Timer? _knifeRoundTimer;

    private CsTeam? _winningTeam;

    public KnifeSystem(
        ILogger<KnifeSystem> logger,
        GameServer gameServer,
        MatchEvents matchEvents,
        MatchService matchService,
        EnvironmentService environmentService,
        IStringLocalizer localizer
    )
    {
        _logger = logger;
        _matchService = matchService;
        _matchEvents = matchEvents;
        _gameServer = gameServer;
        _environmentService = environmentService;
        _localizer = localizer;
    }

    public void Start()
    {
        _gameServer.SendCommands(
            new[] { "exec 5stack.knife.cfg", "mp_warmup_end", "mp_restartgame 1" }
        );

        Server.NextFrame(() =>
        {
            TimerUtility.AddTimer(
                5,
                () => _gameServer.Message(HudDestination.Alert, _localizer["knife.start"])
            );
        });
    }

    public void SetWinningTeam(CsTeam team)
    {
        _gameServer.SendCommands(new[] { "mp_warmup_start;mp_pause_match" });

        var rules = MatchUtility.Rules();
        if (rules != null)
        {
            rules.RoundsPlayedThisPhase = 0;
        }

        _logger.LogInformation($"setting winning team: {team}");

        _winningTeam = team;

        _knifeRoundTimer = TimerUtility.AddTimer(3, SetupKnifeMessage, TimerFlags.REPEAT);

        SetupKnifeMessage();

        _gameServer.Message(
            HudDestination.Alert,
            _localizer["knife.captain_picking", (team == CsTeam.Terrorist ? "Terrorist" : "CT")]
        );
    }

    public void SetupKnifeMessage()
    {
        if (_winningTeam == null)
        {
            _logger.LogCritical("missing winning team");
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
            _localizer[
                "knife.captain_prompt",
                ChatColors.Green,
                CommandUtility.PublicChatTrigger,
                ChatColors.Default,
                ChatColors.Green,
                CommandUtility.PublicChatTrigger
            ]
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
                _localizer["knife.not_captain", ChatColors.Red],
                player
            );
            return;
        }

        ResetKnifeRound();

        _gameServer.Message(
            HudDestination.Alert,
            _localizer["knife.captain_picked_stay", ChatColors.Red, ChatColors.Default]
        );

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

        if (match.captainSystem.IsCaptain(player, winningTeam) == false)
        {
            _gameServer.Message(
                HudDestination.Chat,
                _localizer["knife.not_captain", ChatColors.Red],
                player
            );
            return;
        }

        ResetKnifeRound();

        _gameServer.Message(
            HudDestination.Alert,
            _localizer["knife.captain_picked_swap", ChatColors.Red, ChatColors.Default]
        );

        if (_environmentService.IsOfflineMode())
        {
            _gameServer.SendCommands(new[] { "mp_swapteams" });
            Server.NextFrame(() =>
            {
                match.UpdateMapStatus(eMapStatus.Live);
            });
            return;
        }

        _matchEvents.PublishGameEvent("switch", new Dictionary<string, object>());
    }

    public void Skip()
    {
        _gameServer.SendCommands(new[] { "mp_warmup_start;" });

        var rules = MatchUtility.Rules();
        if (rules != null)
        {
            rules.RoundsPlayedThisPhase = 0;
        }

        ResetKnifeRound();

        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null || !match.IsKnife())
        {
            return;
        }

        _gameServer.Message(HudDestination.Center, _localizer["knife.skipping"]);

        _gameServer.SendCommands(new[] { "mp_restartgame 1;mp_warmup_end" });

        match.UpdateMapStatus(eMapStatus.Live);
    }

    public async void ConfirmSwitch()
    {
        _logger.LogInformation("Knife round confirming switch");

        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null)
        {
            return;
        }

        _gameServer.SendCommands(new[] { "mp_swapteams" });

        await Task.Delay(1 * 1000);

        Server.NextFrame(() =>
        {
            match.UpdateMapStatus(eMapStatus.Live);
        });
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
