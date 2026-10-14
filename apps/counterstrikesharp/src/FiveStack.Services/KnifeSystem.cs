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

    // Ending warmup to start the knife reports whoever is alive as a round
    // win, so with a whole side dead it handed out the knife before anyone
    // fought. Only a round whose freeze time has ended is a knife result.
    private bool _roundLive;

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
        _roundLive = false;

        _gameServer.SendCommands(["exec 5stack.knife.cfg", "mp_warmup_end", "mp_restartgame 1"]);

        Server.NextFrame(() =>
        {
            TimerUtility.AddTimer(
                5,
                () => _gameServer.Message(HudDestination.Alert, _localizer["knife.start"])
            );
        });
    }

    public void MarkRoundLive()
    {
        _roundLive = true;
    }

    public bool IsRoundLive()
    {
        return _roundLive;
    }

    public void SetWinningTeam(CsTeam team)
    {
        _gameServer.SendCommands(["mp_warmup_start;mp_pause_match"]);

        MatchManager? match = _matchService.GetCurrentMatch();

        if (match != null)
        {
            _gameServer.SendCommands(match.MatchConfigExecCommands());
        }

        var rules = MatchUtility.Rules();
        if (rules != null)
        {
            rules.RoundsPlayedThisPhase = 0;
        }

        _logger.LogInformation($"setting winning team: {team}");

        _winningTeam = team;
        _roundLive = false;

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
    // Null when the round was never really played and has to be replayed.
    public CsTeam? ResolveTimedOutKnifeWinner(CsTeam reported)
    {
        int tPlayers = 0;
        int ctPlayers = 0;
        int tAlive = 0;
        int ctAlive = 0;
        int tHealth = 0;
        int ctHealth = 0;

        foreach (CCSPlayerController player in MatchUtility.Players())
        {
            if (player.Team == CsTeam.Terrorist)
            {
                tPlayers++;
            }
            else if (player.Team == CsTeam.CounterTerrorist)
            {
                ctPlayers++;
            }

            CCSPlayerPawn? pawn = player.PlayerPawn?.Value;
            int health = pawn?.Health ?? 0;
            Vector? origin = pawn?.AbsOrigin;

            if (
                health <= 0
                || origin == null
                || SpawnUtility.IsAtWorldOrigin(origin.X, origin.Y)
            )
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

        if (SpawnUtility.IsUnplayedKnifeTimeout(tPlayers, tAlive, ctPlayers, ctAlive))
        {
            _logger.LogWarning(
                $"knife round timed out with a side that never played (T {tAlive}/{tPlayers} alive, CT {ctAlive}/{ctPlayers} alive) -- replaying it"
            );
            return null;
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
            MatchMap? currentMap = match.GetCurrentMap();

            if (currentMap != null)
            {
                TeamUtility.SwapLineupSides(currentMap);
            }

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
            _gameServer.SendCommands(match.MatchConfigExecCommands());
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

        TeamUtility.SwapLineupSides(currentMap);

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
        _roundLive = false;
    }
}
