using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Translation;
using static SwiftlyS2.Shared.Helper;

namespace FiveStack;

public class GameBackUpRounds
{
    private int? _resetRound;
    private readonly ISwiftlyCore _core;
    private readonly MatchEvents _matchEvents;
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly IServiceProvider _serviceProvider;
    private readonly EnvironmentService _environmentService;
    private readonly ILogger<GameBackUpRounds> _logger;
    private readonly ILocalizer _localizer;

    public VoteSystem? restoreRoundVote;

    private string _rootDir = "/opt";

    public GameBackUpRounds(
        ISwiftlyCore core,
        ILogger<GameBackUpRounds> logger,
        MatchEvents matchEvents,
        GameServer gameServer,
        MatchService matchService,
        IServiceProvider serviceProvider,
        EnvironmentService environmentService,
        ILocalizer localizer
    )
    {
        _core = core;
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
            _rootDir = _core.GameDirectory;
        }
    }

    // SwiftlyS2 sets DEFAULT_WRITE_PATH to csgo/ (src/engine/fixes/entrypoint.cpp),
    // so mp_backup_round_file / mp_backup_restore_load_file resolve relative to
    // csgo/ — the same as vanilla CS2. Pass the bare prefix/filename.
    private string BackupDirectory => Path.Join(_core.GameDirectory, "csgo");

    public void RemovePlayerVoteOnDisconnect(ulong steamId)
    {
        restoreRoundVote?.RemovePlayerVote(steamId);
    }

    // Sets a convar via the typed SwiftlyS2 accessor. Returns false (instead of
    // throwing) when the convar doesn't exist or isn't of type T.
    private bool TrySetConVar<T>(string name, T value)
    {
        try
        {
            var conVar = _core.ConVar.Find<T>(name);
            if (conVar == null)
            {
                return false;
            }

            conVar.Value = value;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Setup()
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match == null)
        {
            return;
        }

        string prefix = MatchUtility.GetSafeMatchPrefix(match);

        // mp_backup_round_file is a string convar; mp_backup_round_auto is a bool
        // (int on some engine builds). SwiftlyS2's Find<T> throws on a type mismatch,
        // so set each via the accessor matching its real type.
        var fileConVar = _core.ConVar.FindAsString("mp_backup_round_file");
        if (fileConVar != null)
        {
            fileConVar.ValueAsString = prefix;
        }

        if (!TrySetConVar("mp_backup_round_auto", true) && !TrySetConVar("mp_backup_round_auto", 1))
        {
            _logger.LogWarning("Backup Setup: could not enable mp_backup_round_auto");
        }

        // Read the values straight back from the engine to confirm the sets applied.
        string autoReadback =
            _core.ConVar.FindAsString("mp_backup_round_auto")?.ValueAsString ?? "<null>";
        string fileReadback =
            _core.ConVar.FindAsString("mp_backup_round_file")?.ValueAsString ?? "<null>";

        _logger.LogInformation(
            "Backup Setup: set mp_backup_round_file={Prefix}, mp_backup_round_auto=1 | readback auto={Auto} file={File} (files: {Dir}/{Prefix}_round<NN>.txt)",
            prefix,
            autoReadback,
            fileReadback,
            BackupDirectory,
            prefix
        );
    }

    public bool IsResettingRound()
    {
        return _resetRound != null;
    }

    public void CheckForBackupRestore()
    {
        MatchManager? matchManager = _matchService.GetCurrentMatch();
        MatchData? match = matchManager?.GetMatchData();
        MatchMap? matchMap = matchManager?.GetCurrentMap();

        if (match == null || matchMap == null)
        {
            return;
        }

        // Detect from the backup files on disk (written by mp_backup_round_auto),
        // not just the backend's recorded rounds — the files are the ground truth
        // and exist even when the backend/offline match data has no rounds.
        string csgoDir = BackupDirectory;
        string prefix = MatchUtility.GetSafeMatchPrefix(match);

        int highestNumber = GetHighestBackupRoundOnDisk(csgoDir, prefix);
        int totalRoundsPlayed = _gameServer.GetTotalRoundsPlayed();

        _logger.LogInformation(
            $"Highest Backup Round (disk): {highestNumber}, and total rounds played is {totalRoundsPlayed}"
        );

        // Nothing ahead of the current round — we are live where we should be.
        if (highestNumber <= totalRoundsPlayed)
        {
            return;
        }

        // A backup file exists ahead of the current round (server/match restarted).
        // Load it so the restore flow has it, then prompt to restore.
        string backupFilePath = Path.Join(
            csgoDir,
            $"{prefix}_round{highestNumber.ToString().PadLeft(2, '0')}.txt"
        );

        try
        {
            if (!matchMap.rounds.Any(backupRound => backupRound.round == highestNumber))
            {
                string content = File.ReadAllText(backupFilePath);
                matchMap.rounds = matchMap
                    .rounds.Append(
                        new BackupRound { round = highestNumber, backup_file = content }
                    )
                    .ToArray();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to read backup round file {backupFilePath}");
            return;
        }

        _logger.LogInformation(
            $"Backup round {highestNumber} is ahead of current round {totalRoundsPlayed}, prompting restore"
        );
        RequestRestoreBackupRound(highestNumber, null, true);
    }

    // Scans the game dir for CS2 round backup files ({prefix}_round<NN>.txt) and
    // returns the highest round number present (0 if none).
    private int GetHighestBackupRoundOnDisk(string csgoDir, string prefix)
    {
        int highest = 0;

        if (!Directory.Exists(csgoDir))
        {
            return highest;
        }

        try
        {
            foreach (string file in Directory.GetFiles(csgoDir, $"{prefix}_round*.txt"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                int idx = name.LastIndexOf("_round", StringComparison.Ordinal);
                if (idx < 0)
                {
                    continue;
                }

                string numberPart = name.Substring(idx + "_round".Length);
                if (int.TryParse(numberPart, out int round) && round > highest)
                {
                    highest = round;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed scanning for backup round files");
        }

        return highest;
    }

    // Diagnostic: lists every backup file for this match's prefix with size and
    // seconds-since-write, so the logs reveal at a glance whether a missing round
    // is a race (file present but written <1s ago / a newer file exists) vs a
    // number mismatch (wrong round present) vs a prefix/path problem.
    private string DescribeBackupFiles(string csgoDir, string prefix)
    {
        try
        {
            if (!Directory.Exists(csgoDir))
            {
                return $"<dir missing: {csgoDir}>";
            }

            var forPrefix = Directory.GetFiles(csgoDir, $"{prefix}_round*.txt");
            if (forPrefix.Length == 0)
            {
                var anyRound = Directory
                    .GetFiles(csgoDir, "*_round*.txt")
                    .Select(Path.GetFileName)
                    .ToArray();
                return anyRound.Length > 0
                    ? $"<none for prefix; other prefixes present: {string.Join(", ", anyRound)}>"
                    : "<none>";
            }

            DateTime now = DateTime.UtcNow;
            return string.Join(
                ", ",
                forPrefix
                    .OrderBy(f => f)
                    .Select(f =>
                    {
                        var info = new FileInfo(f);
                        return $"{info.Name}({info.Length}b,{(now - info.LastWriteTimeUtc).TotalSeconds:F1}s)";
                    })
            );
        }
        catch (Exception ex)
        {
            return $"<error: {ex.Message}>";
        }
    }

    public void RequestRestoreBackupRound(
        int round,
        IPlayer? player = null,
        bool vote = false
    )
    {
        _logger.LogInformation($"Restoring Backup Round {round}");
        if (IsResettingRound())
        {
            return;
        }

        if (!CanRestoreRound(round))
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

        _gameServer.SendCommands(["mp_pause_match"]);

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
                new Team[] { Team.CT, Team.T },
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

            if (player != null && restoreRoundVote != null)
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

            string prefix = MatchUtility.GetSafeMatchPrefix(match);
            string backupRoundFilePath = Path.Join(
                BackupDirectory,
                $"{prefix}_round{round.ToString().PadLeft(2, '0')}.txt"
            );

            bool exists = File.Exists(backupRoundFilePath);

            _logger.LogInformation(
                "GetBackupRoundFile: round={Round} totalRoundsPlayed={Total} exists={Exists} path={Path} dir={Dir} files=[{Files}]",
                round,
                _gameServer.GetTotalRoundsPlayed(),
                exists,
                backupRoundFilePath,
                BackupDirectory,
                DescribeBackupFiles(BackupDirectory, prefix)
            );

            if (!exists)
            {
                _logger.LogCritical(
                    "Unable to publish backup round {Round}: file not on disk at read time ({Path}). "
                        + "If a higher-numbered file exists this is a round-number mismatch; if none/newer exists it is a write/read race.",
                    round,
                    backupRoundFilePath
                );
                return null;
            }

            string backupRoundFile = File.ReadAllText(backupRoundFilePath);
            _logger.LogInformation(
                "Read backup round {Round} from {Path} ({Bytes} bytes)",
                round,
                backupRoundFilePath,
                backupRoundFile.Length
            );

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
        MatchManager? matchManager = _matchService.GetCurrentMatch();
        MatchData? match = matchManager?.GetMatchData();
        if (matchManager == null || match?.current_match_map_id == null)
        {
            _logger.LogWarning(
                "Restore round {Round} dropped: no current match or match map (match={HasMatch} current_match_map_id={MapId})",
                round,
                match != null,
                match?.current_match_map_id?.ToString() ?? "<null>"
            );
            return;
        }

        Guid mapId = matchManager.GetActiveMapId() ?? match.current_match_map_id.Value;

        _logger.LogInformation(
            "Publishing restoreRound event to backend (round={Round} match_map_id={MapId} active_map_id={ActiveMapId})",
            round,
            mapId,
            matchManager.GetActiveMapId()?.ToString() ?? "<null>"
        );

        _matchEvents.PublishGameEvent(
            "restoreRound",
            new Dictionary<string, object> { { "round", round }, { "match_map_id", mapId } }
        );
    }

    public void RestoreRound(int round)
    {
        if (IsResettingRound())
        {
            _logger.LogWarning($"Restore already in progress, ignoring RestoreRound({round})");
            return;
        }

        if (!CanRestoreRound(round))
        {
            return;
        }

        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();
        MatchMap? matchMap = _matchService.GetCurrentMatch()?.GetCurrentMap();

        if (match == null || matchMap == null)
        {
            _logger.LogWarning(
                "RestoreRound({Round}) dropped: no current match/map (match={HasMatch} map={HasMap})",
                round,
                match != null,
                matchMap != null
            );
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
            _logger.LogWarning(
                "missing backup round: {Round} (known rounds: [{Rounds}])",
                round,
                string.Join(", ", matchMap.rounds.Select(r => r.round))
            );
            return;
        }

        string backupRoundFileName =
            $"restore-{MatchUtility.GetSafeMatchPrefix(match)}round{round.ToString().PadLeft(2, '0')}.txt";
        string backupRoundFilePath = Path.Join(
            BackupDirectory,
            backupRoundFileName
        );

        _resetRound = round;

        _logger.LogInformation($"Loading backup round file {backupRoundFileName}");

        string backupFileContents = backupRound.backup_file;

        _ = Task.Run(() =>
        {
            try
            {
                File.WriteAllText(backupRoundFilePath, backupFileContents);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed writing restore backup round file {File}",
                    backupRoundFileName
                );
                _core.Scheduler.NextTick(() => _resetRound = null);
                return;
            }

            _core.Scheduler.NextTick(() =>
            {
                _gameServer.SendCommands(
                    [$"mp_backup_restore_load_file {backupRoundFileName}"]
                );
                _matchService.GetCurrentMatch()?.PauseMatch();

                TimerUtility.AddTimer(
                    5,
                    () =>
                    {
                        _resetRound = null;

                        _logger.LogInformation($"Sending Message for Round {round}");

                        _gameServer.Message(
                            MessageType.Alert,
                            _localizer[
                                "backup.round_restored",
                                ChatColors.Red,
                                round,
                                CommandUtility.PublicChatTrigger
                            ]
                        );
                    }
                );
            });
        });
    }

    private bool CanRestoreRound(int round)
    {
        int connectedPlayers = MatchUtility.PlayerCount();
        int expectedPlayers = _matchService.GetCurrentMatch()?.GetExpectedPlayerCount() ?? 10;

        if (connectedPlayers >= expectedPlayers)
        {
            return true;
        }

        _logger.LogWarning(
            $"Restore round {round} blocked: waiting for all players to reconnect ({connectedPlayers}/{expectedPlayers})"
        );
        _gameServer.Message(
            MessageType.Alert,
            $" Restore round blocked: waiting for all players to reconnect ({connectedPlayers}/{expectedPlayers})."
        );

        return false;
    }
}
