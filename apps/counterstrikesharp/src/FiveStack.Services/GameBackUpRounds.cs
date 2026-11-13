using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class GameBackUpRounds
{
    private readonly RoundRestoreState _restore;

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
        IStringLocalizer localizer,
        RoundRestoreState restore
    )
    {
        _restore = restore;
        _restore.LoadStarted += matchEvents.ClearPendingRoundResult;
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

    public void RemovePlayerVoteOnDisconnect(ulong steamId)
    {
        restoreRoundVote?.RemovePlayerVote(steamId);
    }

    public void Setup()
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match == null)
        {
            return;
        }

        ConVar.Find("mp_backup_round_file")?.SetValue(MatchUtility.GetSafeMatchPrefix(match));
    }

    public bool IsResettingRound()
    {
        return _restore.Resetting;
    }

    public bool HoldsRoundResults()
    {
        return RoundRestoreGateUtility.HoldsRoundResults(
            IsResettingRound(),
            restoreRoundVote?.IsVoteActive() == true,
            _restore.HoldingRounds
        );
    }

    public bool IsHoldingForRestore()
    {
        return _restore.HoldingRounds;
    }

    public void SetBackupWriting(bool enabled)
    {
        ConVar.Find("mp_backup_round_auto")?.SetValue(enabled ? 1 : 0);
    }

    // Offers the vote on the round a restarted server is behind by once every
    // player is back. Called when the match loads and as players connect.
    public void OfferPendingRestore()
    {
        if (!_restore.CanOfferVote || restoreRoundVote != null)
        {
            return;
        }

        int round = _restore.HeldRound!.Value;
        int connectedPlayers = MatchUtility.Players().Count;
        int expectedPlayers = _matchService.GetCurrentMatch()?.GetExpectedPlayerCount() ?? 10;

        if (connectedPlayers < expectedPlayers)
        {
            _gameServer.Message(
                HudDestination.Alert,
                $" Waiting for all players ({connectedPlayers}/{expectedPlayers}) to vote on restoring the start of round {RoundRestoreGateUtility.StartOfRound(round)}."
            );
            return;
        }

        _restore.ReleaseHold();
        RequestRestoreBackupRound(round, null, true, recovering: true);

        // Refused for another reason: keep holding for the next player.
        if (!_restore.InProgress && HoldStaysOn(round))
        {
            _restore.Hold(round, ActiveMapId());
        }
    }

    public bool CheckForBackupRestore(eMapStatus status)
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();
        MatchMap? matchMap = _matchService.GetCurrentMatch()?.GetCurrentMap();

        if (matchMap == null)
        {
            return false;
        }

        bool offline = _environmentService.IsOfflineMode() && match != null;
        int highestOnDisk = offline ? HighestBackupRoundOnDisk(match!, matchMap) : 0;

        int highestNumber = RoundRestoreGateUtility.HighestRestorableRound(
            matchMap.rounds,
            highestOnDisk
        );
        int totalRoundsPlayed = _gameServer.GetTotalRoundsPlayed();

        _logger.LogInformation(
            $"Highest Backup Round: {highestNumber}, and total rounds played is {totalRoundsPlayed}"
        );

        // Nothing to restore ahead of the current round.
        if (highestNumber <= totalRoundsPlayed)
        {
            if (
                RoundRestoreGateUtility.HighestRecordedRound(matchMap.rounds, highestOnDisk)
                > totalRoundsPlayed
            )
            {
                return ReplayMapAfterRestart(status);
            }

            return false;
        }

        if (offline && RoundRestoreGateUtility.LatestBackup(matchMap.rounds, highestNumber) == null)
        {
            BackupRound? onDisk = BackupOnDisk(match!, highestNumber);

            if (onDisk == null)
            {
                return false;
            }

            matchMap.rounds = matchMap.rounds.Append(onDisk).ToArray();
        }

        if (_matchService.GetCurrentMatch()?.GetMatchData()?.options.CanRestoreRounds() == false)
        {
            return ReplayMapAfterRestart(status);
        }

        if (!RoundRestoreGateUtility.IsInPlay(status))
        {
            return false;
        }

        _logger.LogInformation(
            $"Backup round {highestNumber} is ahead of current round {totalRoundsPlayed}, holding the match for a restore vote"
        );
        HoldForRestore(highestNumber);

        return false;
    }

    private void HoldForRestore(int round)
    {
        _restore.Hold(round, ActiveMapId());
        SetBackupWriting(false);
        _gameServer.SendCommands(["mp_pause_match"]);
        OfferPendingRestore();
    }

    // A load that is owed and not coming goes back to the hold, and never to a
    // replay: only players deciding that gives up rounds the backend recorded.
    private void ReturnToHold(int round)
    {
        if (!HoldStaysOn(round))
        {
            return;
        }

        _logger.LogWarning($"Round {round} was not restored, holding the match for another vote");
        HoldForRestore(round);
    }

    // False once the hold has moved to another round or ended.
    private bool HoldStaysOn(int round)
    {
        MatchManager? matchManager = _matchService.GetCurrentMatch();
        eMapStatus status = matchManager?.GetCurrentMapStatus() ?? eMapStatus.Unknown;
        int? next = RoundRestoreGateUtility.RoundToHold(
            status,
            matchManager?.GetCurrentMap()?.rounds ?? Array.Empty<BackupRound>(),
            round,
            _gameServer.GetTotalRoundsPlayed()
        );

        if (next == round)
        {
            return true;
        }

        if (RoundRestoreGateUtility.IsInPlay(status))
        {
            _gameServer.Message(
                HudDestination.Alert,
                RoundRestoreGateUtility.WhyTheHoldMoved(round, next)
            );
        }

        if (next is int moved)
        {
            HoldForRestore(moved);
            return false;
        }

        SetBackupWriting(!IsHoldingForRestore());

        return false;
    }

    private void ReturnOwedToHold()
    {
        if (_restore.FailedLoad() is int round)
        {
            ReturnToHold(round);
        }
    }

    // A hold turns backups off itself and offers its vote straight away, so
    // nothing here may read the hold after that.
    private void FailLoad()
    {
        if (_restore.FailedLoad() is int round)
        {
            ReturnToHold(round);
            return;
        }

        SetBackupWriting(!IsHoldingForRestore());
    }

    private Guid? ActiveMapId()
    {
        MatchManager? matchManager = _matchService.GetCurrentMatch();

        return matchManager?.GetActiveMapId()
            ?? matchManager?.GetMatchData()?.current_match_map_id;
    }

    private void OweLoad(int round)
    {
        int owing = _restore.StartOwing(round, ActiveMapId());

        TimerUtility.AddTimer(RoundRestoreState.OwedSeconds, () => OwedLoadTimedOut(owing));
    }

    private void OwedLoadTimedOut(int owing)
    {
        OwedLoadTimeout timeout = _restore.TimedOut(owing, ActiveMapId());

        if (!timeout.Expired)
        {
            return;
        }

        _matchEvents.WithdrawRestoreRequests();

        if (timeout.HoldRound is int round)
        {
            ReturnToHold(round);
            return;
        }

        SetBackupWriting(!IsHoldingForRestore());
    }

    // A dedicated server keeps this state from one match to the next.
    public void ForgetOtherMaps(Guid? map)
    {
        if (_restore.LeaveMap(map))
        {
            restoreRoundVote?.Abandon();
            restoreRoundVote = null;
            SetBackupWriting(!IsHoldingForRestore());
        }
    }

    public void TellWhyResumeWaits()
    {
        string? refusal = RoundRestoreGateUtility.ResumeRefusal(
            _restore.OwedRound,
            _restore.NeedsRestartMap
        );

        if (refusal != null)
        {
            _gameServer.Message(HudDestination.Alert, refusal);
        }
    }

    // Restart Map starts the map over, so whatever a restore was protecting is
    // gone and the restore with it.
    public void AbandonRestore()
    {
        restoreRoundVote?.Abandon();
        restoreRoundVote = null;
        _restore.Abandon();
        SetBackupWriting(true);
    }

    // A match whose rounds can't be restored replays the map from warmup, but
    // only while the map is still being played: a restart after it ended must
    // keep the rounds it recorded.
    private bool ReplayMapAfterRestart(eMapStatus status)
    {
        if (
            status != eMapStatus.Live
            && status != eMapStatus.Overtime
            && status != eMapStatus.Paused
        )
        {
            return false;
        }

        _logger.LogInformation(
            "Server restarted with no round it can restore, replaying the map from warmup"
        );
        ReplayMap(fromWarmup: true);

        return true;
    }

    private void ReplayMap(bool fromWarmup)
    {
        SetBackupWriting(true);
        SendRestoreRoundToBackend(0, fromWarmup);
    }


    public void RequestRestoreBackupRound(
        int round,
        CCSPlayerController? player = null,
        bool vote = false,
        bool recovering = false
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

        string? refusal = RestoreRefusal(round);
        if (refusal != null)
        {
            _logger.LogWarning($"Restore round {round} refused: {refusal}");
            _gameServer.Message(HudDestination.Alert, $" Restore round blocked: {refusal}.");
            return;
        }

        _gameServer.SendCommands(["mp_pause_match"]);

        if (player != null || vote == true)
        {
            _restore.StartVote(recovering, ActiveMapId());

            restoreRoundVote =
                _serviceProvider.GetRequiredService(typeof(VoteSystem)) as VoteSystem;

            if (restoreRoundVote == null)
            {
                return;
            }

            restoreRoundVote.StartVote(
                "backup.vote.restore_to",
                [RoundRestoreGateUtility.StartOfRound(round)],
                new CsTeam[] { CsTeam.CounterTerrorist, CsTeam.Terrorist },
                () =>
                {
                    _logger.LogInformation("restore round vote passed");
                    _restore.End();
                    restoreRoundVote = null;

                    string? refusal = RestoreRefusal(round);
                    if (refusal != null)
                    {
                        _logger.LogWarning(
                            $"Restore round {round} refused after the vote: {refusal}"
                        );
                        _gameServer.Message(
                            HudDestination.Alert,
                            $" Restore round blocked: {refusal}."
                        );
                        if (recovering && HoldStaysOn(round))
                        {
                            _restore.Hold(round, ActiveMapId());
                        }
                        return;
                    }

                    // A round that ended during the vote is the one being undone,
                    // and the next round start can come before the load does.
                    _matchEvents.ClearPendingRoundResult();

                    if (
                        RoundRestoreGateUtility.OwesLoad(
                            recovering,
                            _environmentService.IsOfflineMode()
                        )
                    )
                    {
                        OweLoad(round);
                    }
                    else if (recovering)
                    {
                        SetBackupWriting(true);
                    }

                    SendRestoreRoundToBackend(round);
                },
                () =>
                {
                    _logger.LogInformation("restore round vote failed");

                    restoreRoundVote = null;
                    _restore.End();

                    // Declined after a restart: the map starts again from 0-0 and
                    // the rounds the backend recorded are set aside.
                    if (recovering)
                    {
                        ReplayMap(fromWarmup: false);
                        return;
                    }

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
            _logger.LogError(ex, "An error occurred during backup round upload");
        }
        return null;
    }

    public void SendRestoreRoundToBackend(int round, bool replay = false)
    {
        _logger.LogInformation($"Restoring Round {round}");

        if (_environmentService.IsOfflineMode())
        {
            RestoreRoundOffline(round);
            return;
        }

        MatchManager? matchManager = _matchService.GetCurrentMatch();
        MatchData? match = matchManager?.GetMatchData();
        if (matchManager == null || match?.current_match_map_id == null)
        {
            return;
        }

        Guid mapId = matchManager.GetActiveMapId() ?? match.current_match_map_id.Value;

        _matchEvents.PublishGameEvent(
            "restoreRound",
            MatchEventPayloads.RestoreRound(round, mapId, replay)
        );
    }

    // Offline there is no api to reset the map and answer with api_restore_round,
    // so this does both.
    private void RestoreRoundOffline(int round)
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();
        MatchMap? matchMap = _matchService.GetCurrentMatch()?.GetCurrentMap();

        if (match == null || matchMap == null)
        {
            _logger.LogWarning($"Restore round {round} dropped: no current match or match map");
            return;
        }

        string? refusal = OfflineMatchState.ResetToRound(
            matchMap,
            match.options.IsRush(),
            round,
            DateTime.UtcNow.ToString("o")
        );

        if (refusal != null)
        {
            _logger.LogWarning($"Restore round {round} ignored offline: {refusal}");
            _gameServer.Message(HudDestination.Alert, $" Restore round blocked: {refusal}.");
            return;
        }

        RestoreRound(round);
        _matchService.SaveOfflineProgress();
    }

    private static string BackupDirectory => Path.Join(Server.GameDirectory, "csgo");

    // Offline the backup files are all a restarted server has, as sw reads them.
    private BackupRound? BackupOnDisk(MatchData match, int round)
    {
        string path = Path.Join(
            BackupDirectory,
            $"{MatchUtility.GetSafeMatchPrefix(match)}_round{round.ToString().PadLeft(2, '0')}.txt"
        );

        try
        {
            string content = File.Exists(path) ? File.ReadAllText(path) : "";

            return string.IsNullOrEmpty(content)
                ? null
                : new BackupRound { round = round, backup_file = content };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to read backup round file {path}");
            return null;
        }
    }

    private int HighestBackupRoundOnDisk(MatchData match, MatchMap matchMap)
    {
        int highest = 0;
        string prefix = $"{MatchUtility.GetSafeMatchPrefix(match)}_round";

        if (!Directory.Exists(BackupDirectory))
        {
            return highest;
        }

        try
        {
            foreach (string file in Directory.GetFiles(BackupDirectory, $"{prefix}*.txt"))
            {
                string number = Path.GetFileNameWithoutExtension(file)[prefix.Length..];

                if (
                    int.TryParse(number, out int round)
                    && round > highest
                    && RoundRestoreGateUtility.IsUsableOnDisk(matchMap.rounds, round, true)
                )
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

    public void RestoreRound(int round)
    {
        if (_restore.VotingOrLoading)
        {
            _logger.LogWarning($"Restore already in progress, ignoring RestoreRound({round})");
            return;
        }

        if (!CanRestoreRound(round))
        {
            ReturnOwedToHold();
            return;
        }

        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();
        MatchMap? matchMap = _matchService.GetCurrentMatch()?.GetCurrentMap();

        if (match == null || matchMap == null)
        {
            ReturnOwedToHold();
            return;
        }

        BackupRound? backupRound = BackupOf(match, matchMap, round);

        if (backupRound == null)
        {
            _logger.LogWarning($"missing backup round: {round}");
            ReturnOwedToHold();
            return;
        }

        string backupRoundFileName =
            $"restore-{MatchUtility.GetSafeMatchPrefix(match)}round{round.ToString().PadLeft(2, '0')}.txt";
        string backupRoundFilePath = Path.Join(
            Server.GameDirectory + "/csgo/",
            backupRoundFileName
        );

        int? held = _restore.ReleaseHold();

        _restore.StartLoad(held);

        try
        {
            File.WriteAllText(backupRoundFilePath, backupRound.backup_file);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed writing restore backup round file {File}",
                backupRoundFileName
            );
            FailLoad();
            return;
        }

        _logger.LogInformation($"Loading backup round file {backupRoundFileName}");

        Server.NextFrame(() =>
        {
            _ = Task.Delay(5 * 1000)
                .ContinueWith(_ =>
                    Server.NextFrame(() =>
                    {
                        _restore.End();
                        SetBackupWriting(true);

                        _logger.LogInformation($"Sending Message for Round {round}");

                        _gameServer.Message(
                            HudDestination.Alert,
                            _localizer[
                                "backup.round_restored",
                                ChatColors.Red,
                                RoundRestoreGateUtility.StartOfRound(round),
                                CommandUtility.PublicChatTrigger
                            ]
                        );
                    })
                );

            _gameServer.SendCommands([$"mp_backup_restore_load_file {backupRoundFileName}"]);
            _matchService.GetCurrentMatch()?.PauseMatch();
        });
    }

    // restore_round hands the round to the api, which marks the later rounds'
    // stats deleted before it sends api_restore_round back, so a restore that
    // would be refused there has to be refused before it leaves.
    public string? RestoreRefusal(int round)
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();
        MatchMap? matchMap = _matchService.GetCurrentMatch()?.GetCurrentMap();

        return RoundRestoreGateUtility.Refusal(
            match != null && !match.options.CanRestoreRounds(),
            _restore.VotingOrLoading,
            _matchService.GetCurrentMatch()?.GetCurrentMapStatus() ?? eMapStatus.Unknown,
            round,
            match != null && matchMap != null && BackupOf(match, matchMap, round) != null,
            MatchUtility.Players().Count,
            _matchService.GetCurrentMatch()?.GetExpectedPlayerCount() ?? 10,
            match?.options.type ?? ""
        );
    }

    private BackupRound? BackupOf(MatchData match, MatchMap matchMap, int round)
    {
        BackupRound? kept = RoundRestoreGateUtility.LatestBackup(matchMap.rounds, round);
        eMapStatus status =
            _matchService.GetCurrentMatch()?.GetCurrentMapStatus() ?? eMapStatus.Unknown;

        if (kept != null || !RoundRestoreGateUtility.ReadsBackupFromDisk(round, status))
        {
            return kept;
        }

        return BackupOnDisk(match, round);
    }

    public bool RoundRestoresDisabled()
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match == null || match.options.CanRestoreRounds())
        {
            return false;
        }

        _logger.LogWarning($"Restore round blocked: {match.options.type} matches can't restore rounds");
        _gameServer.Message(
            HudDestination.Alert,
            $" Round restores are disabled in {match.options.type}."
        );

        return true;
    }

    private bool CanRestoreRound(int round)
    {
        if (RoundRestoresDisabled())
        {
            return false;
        }

        int connectedPlayers = MatchUtility.Players().Count;
        int expectedPlayers = _matchService.GetCurrentMatch()?.GetExpectedPlayerCount() ?? 10;

        if (connectedPlayers >= expectedPlayers)
        {
            return true;
        }

        _logger.LogWarning(
            $"Restore round {round} blocked: waiting for all players to reconnect ({connectedPlayers}/{expectedPlayers})"
        );
        _gameServer.Message(
            HudDestination.Alert,
            $" Restore round blocked: waiting for all players to reconnect ({connectedPlayers}/{expectedPlayers})."
        );

        return false;
    }
}
