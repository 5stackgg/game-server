using FiveStack.Entities;
using FiveStack.Enums;
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

    // A restore that has to happen before play may continue: the backend knows
    // rounds this game does not (fresh process, map reload), or an organizer
    // asked for one while the roster was short.
    private int? _pendingRestoreRound;
    private int? _forcedRestoreRound;
    private bool _recoveryNeedsOrganizer;
    private bool _behindNeedsConfirmation;
    private int _restoreAttempts;
    private Guid? _syncedMapId;
    private DateTime _lastRestoreRequestAt = DateTime.MinValue;
    private CancellationTokenSource? _recoveryTimer;

    private const int RecoveryTickSeconds = 5;
    private const int RestoreRequestRetrySeconds = 15;
    private const int MaxRestoreAttempts = 3;

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

    public void Reset()
    {
        TimerUtility.Kill(_recoveryTimer);
        _recoveryTimer = null;
        _resetRound = null;
        _pendingRestoreRound = null;
        _forcedRestoreRound = null;
        _recoveryNeedsOrganizer = false;
        _behindNeedsConfirmation = false;
        _restoreAttempts = 0;
        _syncedMapId = null;
        restoreRoundVote = null;
    }

    public bool IsRecoveryPending()
    {
        return _pendingRestoreRound != null || _recoveryNeedsOrganizer;
    }

    // Nothing that advances the match -- resuming, capturing or publishing a
    // round -- may run while the game state is not the backend's.
    public bool BlocksPlay()
    {
        return IsResettingRound() || IsRecoveryPending();
    }

    private int MinPlayersPerTeam()
    {
        return BackupRoundUtility.MinPlayersPerTeam(
            _matchService.GetCurrentMatch()?.GetExpectedPlayerCount() ?? 10
        );
    }

    // Runs on every match setup, not just the first: a map reload mid-match
    // leaves a long-lived process exactly as far behind as a crash does.
    public void CheckForBackupRestore()
    {
        MatchManager? matchManager = _matchService.GetCurrentMatch();
        MatchData? match = matchManager?.GetMatchData();
        MatchMap? matchMap = matchManager?.GetCurrentMap();

        if (matchManager == null || match == null || matchMap == null || BlocksPlay())
        {
            return;
        }

        eMapStatus backendStatus = MatchUtility.MapStatusStringToEnum(matchMap.status);
        if (
            backendStatus != eMapStatus.Live
            && backendStatus != eMapStatus.Paused
            && backendStatus != eMapStatus.Overtime
        )
        {
            return;
        }

        if (_environmentService.IsOfflineMode())
        {
            LoadBackupRoundsFromDisk(match, matchMap);
        }

        int highestRound = BackupRoundUtility.HighestRound(matchMap.rounds);
        int totalRoundsPlayed = _gameServer.GetTotalRoundsPlayed();

        _logger.LogInformation(
            $"Highest recorded round: {highestRound}, and total rounds played is {totalRoundsPlayed}"
        );

        if (highestRound <= totalRoundsPlayed)
        {
            _syncedMapId = matchMap.id;
            _behindNeedsConfirmation = false;
            return;
        }

        // A process that was in step a moment ago only looks behind if this
        // match data predates a restore it just ran. Believe it on a second,
        // fresh read; a new process has nothing to be stale against.
        if (_syncedMapId == matchMap.id && !_behindNeedsConfirmation)
        {
            _behindNeedsConfirmation = true;
            _logger.LogWarning(
                $"Game is at round {totalRoundsPlayed} but round {highestRound} is recorded, confirming with a fresh match fetch"
            );
            _matchService.GetMatchFromApi();
            return;
        }

        _behindNeedsConfirmation = false;

        int restorableRound = BackupRoundUtility.HighestRestorableRound(
            matchMap.rounds,
            MinPlayersPerTeam()
        );

        if (restorableRound <= totalRoundsPlayed)
        {
            _logger.LogCritical(
                $"Game is at round {totalRoundsPlayed} but round {highestRound} is recorded, and no recorded round has a usable backup"
            );
            _recoveryNeedsOrganizer = true;
        }
        else
        {
            if (restorableRound < highestRound)
            {
                _logger.LogCritical(
                    $"Rounds {restorableRound + 1}-{highestRound} have no usable backup, recovering to round {restorableRound}"
                );
            }

            _logger.LogWarning(
                $"Game is at round {totalRoundsPlayed} but round {highestRound} is recorded, holding the match until round {restorableRound} is restored"
            );
            _pendingRestoreRound = restorableRound;
        }

        _restoreAttempts = 0;
        StartRecoveryTimer();
    }

    private void StartRecoveryTimer()
    {
        TimerUtility.Kill(_recoveryTimer);
        _recoveryTimer = TimerUtility.Repeat(RecoveryTickSeconds, RecoveryTick);
    }

    private void RecoveryTick()
    {
        if (!IsRecoveryPending())
        {
            TimerUtility.Kill(_recoveryTimer);
            _recoveryTimer = null;
            return;
        }

        if (IsResettingRound())
        {
            return;
        }

        MatchManager? match = _matchService.GetCurrentMatch();
        if (match == null || !match.IsInPlay())
        {
            return;
        }

        // Held by CS2 as well as by the plugin: a pause a restart dropped
        // would otherwise let rounds play out that can never count.
        _gameServer.SendCommands(["mp_pause_match"]);
        if (!match.IsPaused())
        {
            match.PauseMatch();
        }

        if (_recoveryNeedsOrganizer)
        {
            _gameServer.Message(
                MessageType.Alert,
                " No usable round backup. An organizer must run restore_round <round> (0 restarts the map)."
            );
            return;
        }

        int round = _pendingRestoreRound!.Value;

        if (!IsRosterWhole(out int connected, out int expected) && _forcedRestoreRound != round)
        {
            _gameServer.Message(
                MessageType.Alert,
                $" Round {round} will be restored once everyone is back ({connected}/{expected}). {CommandUtility.PublicChatTrigger}resume to vote to restore now."
            );
            return;
        }

        if ((DateTime.UtcNow - _lastRestoreRequestAt).TotalSeconds < RestoreRequestRetrySeconds)
        {
            return;
        }

        RequestPendingRestore();
    }

    private void RequestPendingRestore()
    {
        if (_pendingRestoreRound == null)
        {
            return;
        }

        _lastRestoreRequestAt = DateTime.UtcNow;

        if (_environmentService.IsOfflineMode())
        {
            RestoreRound(_pendingRestoreRound.Value);
            return;
        }

        // Through the backend, not straight to CS2: it voids the stats of the
        // round that was cut short before that round is played again.
        SendRestoreRoundToBackend(_pendingRestoreRound.Value);
    }

    // The way out when someone is not coming back: .resume during a recovery
    // asks to restore with whoever is here rather than to unpause a game
    // that must not be played.
    public void RequestRecoveryNow(IPlayer? player, bool isAdmin)
    {
        if (_recoveryNeedsOrganizer || _pendingRestoreRound == null)
        {
            _gameServer.Message(
                MessageType.Chat,
                $" {ChatColors.Red}No usable round backup. An organizer must run restore_round <round>.",
                player
            );
            return;
        }

        int round = _pendingRestoreRound.Value;

        if (IsResettingRound())
        {
            return;
        }

        if (player == null || isAdmin || IsRosterWhole(out _, out _))
        {
            restoreRoundVote?.CancelVote();
            _forcedRestoreRound = round;
            RequestPendingRestore();
            return;
        }

        if (restoreRoundVote != null)
        {
            restoreRoundVote.CastVote(player, true);
            return;
        }

        restoreRoundVote = _serviceProvider.GetRequiredService(typeof(VoteSystem)) as VoteSystem;

        if (restoreRoundVote == null)
        {
            return;
        }

        restoreRoundVote.StartVote(
            $"Restore round {round} without waiting for everyone",
            new Team[] { Team.CT, Team.T },
            () =>
            {
                _logger.LogInformation("restore without full roster vote passed");
                restoreRoundVote = null;
                _forcedRestoreRound = round;
                RequestPendingRestore();
            },
            () =>
            {
                _logger.LogInformation("restore without full roster vote failed");
                restoreRoundVote = null;
            },
            true,
            30
        );

        restoreRoundVote?.CastVote(player, true);
    }

    // Offline matches have no backend to hold the rounds; the files CS2 wrote
    // are all there is. Online they are never trusted past the backend: a
    // file for a round whose score was never published would restore a round
    // the backend has no record of.
    private void LoadBackupRoundsFromDisk(MatchData match, MatchMap matchMap)
    {
        string csgoDir = BackupDirectory;
        string prefix = MatchUtility.GetSafeMatchPrefix(match);

        if (!Directory.Exists(csgoDir))
        {
            return;
        }

        try
        {
            foreach (string file in Directory.GetFiles(csgoDir, $"{prefix}_round*.txt"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                int idx = name.LastIndexOf("_round", StringComparison.Ordinal);
                if (
                    idx < 0
                    || !int.TryParse(name.Substring(idx + "_round".Length), out int round)
                    || matchMap.rounds.Any(backupRound => backupRound.round == round)
                )
                {
                    continue;
                }

                matchMap.rounds = BackupRoundUtility.Upsert(
                    matchMap.rounds,
                    new BackupRound { round = round, backup_file = File.ReadAllText(file) }
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed scanning for backup round files");
        }
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

        if (
            BackupRoundUtility.FindRestorable(matchMap.rounds, round, MinPlayersPerTeam()) == null
        )
        {
            _logger.LogWarning($"no usable backup for round: {round}");
            _gameServer.Message(
                MessageType.Chat,
                $" {ChatColors.Red}Round {round} has no usable backup.",
                player
            );
            return;
        }

        _gameServer.SendCommands(["mp_pause_match"]);

        if (player != null || vote == true)
        {
            restoreRoundVote =
                _serviceProvider.GetRequiredService(typeof(VoteSystem)) as VoteSystem;

            if (restoreRoundVote == null)
            {
                return;
            }

            _resetRound = round;

            restoreRoundVote.StartVote(
                _localizer["backup.vote.restore_to", round],
                new Team[] { Team.CT, Team.T },
                () =>
                {
                    _logger.LogInformation("restore round vote passed");
                    restoreRoundVote = null;
                    _resetRound = null;
                    SendRestoreRoundToBackend(round);
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

            string? invalidReason = BackupRoundUtility.Validate(
                backupRoundFile,
                round,
                MinPlayersPerTeam()
            );

            if (invalidReason != null)
            {
                _logger.LogCritical(
                    "Not publishing backup round {Round}: {Reason} ({Path})",
                    round,
                    invalidReason,
                    backupRoundFilePath
                );
                return null;
            }

            MatchMap? currentMap = _matchService.GetCurrentMatch()?.GetCurrentMap();

            if (currentMap != null)
            {
                currentMap.rounds = BackupRoundUtility.Upsert(
                    currentMap.rounds,
                    new BackupRound { round = round, backup_file = backupRoundFile }
                );
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

    public void RestoreRound(int round, bool force = false)
    {
        if (IsResettingRound())
        {
            _logger.LogWarning($"Restore already in progress, ignoring RestoreRound({round})");
            return;
        }

        MatchManager? matchManager = _matchService.GetCurrentMatch();
        MatchData? match = matchManager?.GetMatchData();
        MatchMap? matchMap = matchManager?.GetCurrentMap();

        if (matchManager == null || match == null || matchMap == null)
        {
            _logger.LogWarning(
                "RestoreRound({Round}) dropped: no current match/map (match={HasMatch} map={HasMap})",
                round,
                match != null,
                matchMap != null
            );
            return;
        }

        if (round == 0)
        {
            RestartMap(matchMap);
            return;
        }

        BackupRound? backupRound = BackupRoundUtility.FindRestorable(
            matchMap.rounds,
            round,
            MinPlayersPerTeam()
        );

        if (backupRound == null)
        {
            _logger.LogWarning(
                "no usable backup for round: {Round} (known rounds: [{Rounds}])",
                round,
                string.Join(", ", matchMap.rounds.Select(r => r.round))
            );
            _gameServer.Message(
                MessageType.Alert,
                $" Round {round} has no usable backup and was not restored."
            );
            return;
        }

        // Queued, never dropped: the backend has already voided the rounds
        // past this one, so giving up here would strand the match between
        // the two.
        if (!force && _forcedRestoreRound != round && !IsRosterWhole(out _, out _))
        {
            _logger.LogWarning($"Restore round {round} queued until the roster is whole");
            _recoveryNeedsOrganizer = false;
            _pendingRestoreRound = round;
            _restoreAttempts = 0;
            _lastRestoreRequestAt = DateTime.UtcNow;
            StartRecoveryTimer();
            RecoveryTick();
            return;
        }

        string backupRoundFileName =
            $"restore-{MatchUtility.GetSafeMatchPrefix(match)}round{round.ToString().PadLeft(2, '0')}.txt";
        string backupRoundFilePath = Path.Join(
            BackupDirectory,
            backupRoundFileName
        );

        // Pending first: cancelling a vote runs its failure path, which
        // resumes the match unless something is already holding it.
        _pendingRestoreRound = round;
        _recoveryNeedsOrganizer = false;
        restoreRoundVote?.CancelVote();
        restoreRoundVote = null;

        _resetRound = round;
        _restoreAttempts++;

        _matchEvents.ClearPendingRoundResult();
        matchMap.rounds = BackupRoundUtility.DropAbove(matchMap.rounds, round);

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
                _core.Scheduler.NextTick(() => FinishRestore(round));
                return;
            }

            _core.Scheduler.NextTick(() =>
            {
                _gameServer.SendCommands(
                    [$"mp_backup_restore_load_file {backupRoundFileName}"]
                );
                _matchService.GetCurrentMatch()?.PauseMatch();

                TimerUtility.AddTimer(5, () => FinishRestore(round));
            });
        });
    }

    // The restore is only over once CS2 is actually at that round. Anything
    // else -- an unwritable file, a load CS2 refused -- leaves the recovery
    // pending so it is retried rather than played through.
    private void FinishRestore(int round)
    {
        _resetRound = null;

        MatchManager? match = _matchService.GetCurrentMatch();
        int totalRoundsPlayed = _gameServer.GetTotalRoundsPlayed();

        if (totalRoundsPlayed != round)
        {
            _logger.LogCritical(
                $"Restore of round {round} did not take: game is at round {totalRoundsPlayed} (attempt {_restoreAttempts}/{MaxRestoreAttempts})"
            );

            if (_restoreAttempts >= MaxRestoreAttempts)
            {
                _pendingRestoreRound = null;
                _recoveryNeedsOrganizer = true;
            }

            _lastRestoreRequestAt = DateTime.UtcNow;
            StartRecoveryTimer();
            return;
        }

        _pendingRestoreRound = null;
        _forcedRestoreRound = null;
        _restoreAttempts = 0;
        _behindNeedsConfirmation = false;
        _syncedMapId = match?.GetCurrentMap()?.id;

        // CS2 seats players from the file; anyone it could not place is put
        // back where the lineups say they belong.
        if (match != null)
        {
            foreach (IPlayer player in MatchUtility.Players())
            {
                match.EnforceMemberTeam(player);
            }
        }

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

    // restore_round 0: the backend has voided every round, so the map starts
    // over. The only way forward when no round has a usable backup.
    private void RestartMap(MatchMap matchMap)
    {
        _logger.LogWarning("Restoring to round 0: restarting the map");

        restoreRoundVote?.CancelVote();
        _matchEvents.ClearPendingRoundResult();
        matchMap.rounds = new BackupRound[0];

        _pendingRestoreRound = null;
        _forcedRestoreRound = null;
        _recoveryNeedsOrganizer = false;
        _behindNeedsConfirmation = false;
        _restoreAttempts = 0;
        _syncedMapId = matchMap.id;

        _gameServer.SendCommands(["mp_restartgame 1"]);
        _matchService.GetCurrentMatch()?.PauseMatch();
    }

    // Casters and admins never make a match whole. Placeholder lineups have
    // no steam ids to match against, so there a head count is the best there is.
    private bool IsRosterWhole(out int connected, out int expected)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        expected = match?.GetExpectedPlayerCount() ?? 10;
        connected =
            matchData == null || MatchUtility.HasPlaceholderMembers(matchData)
                ? MatchUtility.PlayerCount()
                : MatchUtility.ConnectedRosterCount(matchData);

        return connected >= expected;
    }

    private bool CanRestoreRound(int round)
    {
        if (IsRosterWhole(out int connectedPlayers, out int expectedPlayers))
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
