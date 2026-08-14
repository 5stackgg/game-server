using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
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
        _gameServer.SendCommands(["exec 5stack.knife.cfg", "mp_warmup_end", "mp_restartgame 1"]);

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
        _gameServer.SendCommands(["mp_warmup_start;mp_pause_match"]);

        MatchManager? match = _matchService.GetCurrentMatch();

        if (match != null)
        {
            _gameServer.SendCommands([
                $"exec 5stack.{match.GetMatchData()?.options.type.ToLower()}.cfg",
            ]);
        }

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

        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null)
        {
            _logger.LogCritical("missing match");
            return;
        }

        match?.captainSystem?.AutoSelectCaptains();
        CCSPlayerController? captain = match?.captainSystem?.GetTeamCaptain(_winningTeam.Value);

        if (captain == null)
        {
            _logger.LogCritical("missing team captain, auto selecting captains failed");
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

    // A knife round that runs out of time with nobody dead is reported by CS2 as
    // a CT win -- that is just the "target saved" rule, and it hands the side
    // decision to CT for doing nothing. Resolve it on the round's actual state
    // instead: most players alive, then most total health, then a coin flip.
    public CsTeam ResolveTimedOutKnifeWinner(CsTeam reported)
    {
        int tAlive = 0;
        int ctAlive = 0;
        int tHealth = 0;
        int ctHealth = 0;

        foreach (CCSPlayerController player in MatchUtility.Players())
        {
            int health = player.PlayerPawn?.Value?.Health ?? 0;

            if (health <= 0)
            {
                continue;
            }

            if (player.Team == CsTeam.Terrorist)
            {
                tAlive++;
                tHealth += health;
            }
            else if (player.Team == CsTeam.CounterTerrorist)
            {
                ctAlive++;
                ctHealth += health;
            }
        }

        if (tAlive != ctAlive)
        {
            return tAlive > ctAlive ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
        }

        if (tHealth != ctHealth)
        {
            return tHealth > ctHealth ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
        }

        CsTeam coinFlip =
            Random.Shared.Next(2) == 0 ? CsTeam.Terrorist : CsTeam.CounterTerrorist;

        _logger.LogInformation(
            $"knife round tied on players and health -- coin flip gave it to {coinFlip} (CS2 reported {reported})"
        );

        return coinFlip;
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

        Reset();

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

        Reset();

        _gameServer.Message(
            HudDestination.Alert,
            _localizer["knife.captain_picked_swap", ChatColors.Red, ChatColors.Default]
        );

        if (_environmentService.IsOfflineMode())
        {
            match.UpdateMapStatus(eMapStatus.Live);
            _gameServer.SendCommands(["mp_swapteams; mp_restartgame 1"]);
            return;
        }

        _matchEvents.PublishGameEvent("switch", new Dictionary<string, object>());
    }

    public void Skip()
    {
        _gameServer.SendCommands(["mp_warmup_start;"]);

        MatchManager? match = _matchService.GetCurrentMatch();

        if (match != null)
        {
            _gameServer.SendCommands([
                $"exec 5stack.{match.GetMatchData()?.options.type.ToLower()}.cfg",
            ]);
        }

        var rules = MatchUtility.Rules();
        if (rules != null)
        {
            rules.RoundsPlayedThisPhase = 0;
        }

        Reset();

        if (match == null || !match.IsKnife())
        {
            return;
        }

        _gameServer.Message(HudDestination.Center, _localizer["knife.skipping"]);

        _gameServer.SendCommands(["mp_restartgame 1;mp_warmup_end"]);

        match.UpdateMapStatus(eMapStatus.Live);
    }

    public void ConfirmSwitch()
    {
        _logger.LogInformation("Knife round confirming switch");

        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();
        MatchMap? currentMap = match?.GetCurrentMap();

        if (match == null || matchData == null || currentMap == null)
        {
            return;
        }

        currentMap.lineup_1_side = currentMap.lineup_1_side == "CT" ? "TERRORIST" : "CT";
        currentMap.lineup_2_side = currentMap.lineup_2_side == "CT" ? "TERRORIST" : "CT";

        _gameServer.SendCommands(["mp_swapteams"]);

        Server.NextFrame(() =>
        {
            match.UpdateMapStatus(eMapStatus.Live);
        });
        TimerUtility.AddTimer(
            1.0f,
            () =>
            {
                _gameServer.SendCommands(["mp_restartgame 1"]);
            }
        );
    }

    public CsTeam? GetWinningTeam()
    {
        return _winningTeam;
    }

    public void Reset()
    {
        _knifeRoundTimer?.Kill();
        _knifeRoundTimer = null;
        _winningTeam = null;
    }
}
