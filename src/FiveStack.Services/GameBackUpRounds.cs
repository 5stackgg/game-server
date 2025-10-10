using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class GameBackUpRounds
{
    private int? _resetRound;
    private readonly MatchEvents _matchEvents;
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly IServiceProvider _serviceProvider;
    private readonly EnvironmentService _environmentService;
    private readonly ILogger<GameBackUpRounds> _logger;
    private readonly IStringLocalizer _localizer;

    public VoteSystem? restoreRoundVote;

    private string _rootDir = "/opt";

    public GameBackUpRounds(
        ILogger<GameBackUpRounds> logger,
        MatchEvents matchEvents,
        GameServer gameServer,
        MatchService matchService,
        IServiceProvider serviceProvider,
        EnvironmentService environmentService,
        IStringLocalizer localizer
    )
    {
        _logger = logger;
        _matchEvents = matchEvents;
        _gameServer = gameServer;
        _matchService = matchService;
        _serviceProvider = serviceProvider;
        _environmentService = environmentService;
        _localizer = localizer;

        if (
            !Directory.Exists(_rootDir)
            || new DirectoryInfo(_rootDir).Attributes.HasFlag(FileAttributes.ReadOnly)
        )
        {
            _rootDir = Server.GameDirectory;
        }
    }

    public void Setup()
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match == null)
        {
            return;
        }

        _gameServer.SendCommands(
            new[] { $"mp_backup_round_file {MatchUtility.GetSafeMatchPrefix(match)}" }
        );
    }

    public bool IsResettingRound()
    {
        return _resetRound != null;
    }

    public void CheckForBackupRestore()
    {
        MatchMap? matchMap = _matchService.GetCurrentMatch()?.GetCurrentMap();

        if (matchMap == null)
        {
            return;
        }

        var availableRounds = matchMap.rounds.Where(
            (backupRound) =>
            {
                return backupRound.deleted_at == null;
            }
        );

        if (availableRounds.Count() == 0)
        {
            return;
        }

        int highestNumber = availableRounds.Max(
            (backupRound) =>
            {
                return backupRound.round;
            }
        );

        int currentRound = _gameServer.GetCurrentRound();

        _logger.LogInformation(
            $"Highest Backup Round: {highestNumber}, and current round is {currentRound}"
        );

        if (currentRound > 0 && currentRound >= highestNumber)
        {
            // we are already live, do not restart the match accidently
            return;
        }

        if (highestNumber > currentRound)
        {
            _logger.LogInformation("Server restarted, requires a vote to restore round");
            RequestRestoreBackupRound(highestNumber, null, true);
        }
    }

    public void RequestRestoreBackupRound(
        int round,
        CCSPlayerController? player = null,
        bool vote = false
    )
    {
        _logger.LogInformation($"Restoring Backup Round {round}");
        if (IsResettingRound())
        {
            return;
        }

        MatchMap? matchMap = _matchService.GetCurrentMatch()?.GetCurrentMap();

        if (matchMap == null)
        {
            return;
        }

        BackupRound? backupRound = matchMap.rounds.FirstOrDefault(
            (backupRound) =>
            {
                return backupRound.round == round;
            }
        );

        if (backupRound == null)
        {
            _logger.LogWarning($"missing backup round: {round}");
            return;
        }

        _gameServer.SendCommands(new[] { "mp_pause_match" });

        if (player != null || vote == true)
        {
            _resetRound = round;

            restoreRoundVote =
                _serviceProvider.GetRequiredService(typeof(VoteSystem)) as VoteSystem;

            if (restoreRoundVote == null)
            {
                return;
            }

            restoreRoundVote.StartVote(
                _localizer["backup.vote.restore_to", round],
                new CsTeam[] { CsTeam.CounterTerrorist, CsTeam.Terrorist },
                () =>
                {
                    _logger.LogInformation("restore round vote passed");
                    SendRestoreRoundToBackend(round);
                    _resetRound = null;
                },
                () =>
                {
                    _logger.LogInformation("restore round vote failed");

                    restoreRoundVote = null;
                    _resetRound = null;

                    _matchService.GetCurrentMatch()?.ResumeMatch();
                },
                true
            );

            if (player != null)
            {
                restoreRoundVote.CastVote(player, true);
            }

            return;
        }
        SendRestoreRoundToBackend(round);
    }

    public string? GetBackupRoundFile(int round)
    {
        try
        {
            MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();
            if (match == null)
            {
                return null;
            }

            string? serverId = _environmentService.GetServerId();
            string? apiPassword = _environmentService.GetServerApiPassword();

            if (serverId == null || apiPassword == null)
            {
                _logger.LogCritical(
                    $"Unable to upload backup round because we're missing server id / api password"
                );
                return null;
            }

            string backupRoundFilePath = Path.Join(
                Server.GameDirectory + "/csgo/",
                $"{MatchUtility.GetSafeMatchPrefix(match)}_round{round.ToString().PadLeft(2, '0')}.txt"
            );

            if (!File.Exists(backupRoundFilePath))
            {
                _logger.LogCritical(
                    $"Unable to upload backup round because it's missing {backupRoundFilePath}"
                );
                return null;
            }

            string backupRoundFile = File.ReadAllText(backupRoundFilePath);

            MatchMap? currentMap = _matchService.GetCurrentMatch()?.GetCurrentMap();

            if (currentMap != null)
            {
                currentMap.rounds = currentMap
                    .rounds.Append(new BackupRound { round = round, backup_file = backupRoundFile })
                    .ToArray();
            }

            return backupRoundFile;
        }
        catch (Exception ex)
        {
            _logger.LogError($"An error occurred during backup round upload: {ex.Message}");
        }
        return null;
    }

    public void SendRestoreRoundToBackend(int round)
    {
        _logger.LogInformation($"Restoring Round {round}");
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();
        if (match?.current_match_map_id == null)
        {
            return;
        }

        _matchEvents.PublishGameEvent(
            "restoreRound",
            new Dictionary<string, object>
            {
                { "round", round },
                { "match_map_id", match.current_match_map_id },
            }
        );
    }

    public async void RestoreRound(int round)
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();
        MatchMap? matchMap = _matchService.GetCurrentMatch()?.GetCurrentMap();

        if (match == null || matchMap == null)
        {
            return;
        }

        BackupRound? backupRound = matchMap.rounds.FirstOrDefault(
            (backupRound) =>
            {
                return backupRound.round == round;
            }
        );

        if (backupRound == null)
        {
            _logger.LogWarning($"missing backup round: {round}");
            return;
        }

        string backupRoundFilePath = Path.Join(
            Server.GameDirectory + "/csgo/",
            $"restore-{MatchUtility.GetSafeMatchPrefix(match)}round{round.ToString().PadLeft(2, '0')}.txt"
        );

        File.WriteAllText(backupRoundFilePath, backupRound.backup_file);

        _gameServer.SendCommands(new[] { $"mp_backup_restore_load_file {backupRoundFilePath}" });

        _matchService.GetCurrentMatch()?.PauseMatch();

        await Task.Delay(5 * 1000);

        Server.NextFrame(() =>
        {
            _logger.LogInformation($"Sending Message for Round {round}");
            _gameServer.Message(
                HudDestination.Alert,
                _localizer[
                    "backup.round_restored",
                    ChatColors.Red,
                    round,
                    CommandUtility.PublicChatTrigger
                ]
            );
        });
    }
}
