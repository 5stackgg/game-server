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
                $" Waiting for all players ({connectedPlayers}/{expectedPlayers}) to vote on restoring round {round}."
            );
            return;
        }

        _restore.ReleaseHold();
        RequestRestoreBackupRound(round, null, true, recovering: true);

        // Refused for another reason: keep holding for the next player.
        if (!_restore.InProgress)
        {
            _restore.Hold(round);
        }
    }

    public bool CheckForBackupRestore(eMapStatus status)
    {
        MatchMap? matchMap = _matchService.GetCurrentMatch()?.GetCurrentMap();

        if (matchMap == null)
        {
            return false;
        }

        int highestNumber = RoundRestoreGateUtility.HighestRestorableRound(matchMap.rounds, 0);
        int totalRoundsPlayed = _gameServer.GetTotalRoundsPlayed();

        _logger.LogInformation(
            $"Highest Backup Round: {highestNumber}, and total rounds played is {totalRoundsPlayed}"
        );

        // Nothing to restore ahead of the current round.
        if (highestNumber <= totalRoundsPlayed)
        {
            if (RoundRestoreGateUtility.HighestRecordedRound(matchMap.rounds, 0) > totalRoundsPlayed)
            {
                return ReplayMapAfterRestart(status);
            }

            return false;
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
        _restore.Hold(round);
        SetBackupWriting(false);
        _gameServer.SendCommands(["mp_pause_match"]);
        OfferPendingRestore();
    }

    // A load that is owed and not coming goes back to the hold, and never to a
    // replay: only players deciding that gives up rounds the backend recorded.
    private void ReturnToHold(int round)
    {
        eMapStatus status =
            _matchService.GetCurrentMatch()?.GetCurrentMapStatus() ?? eMapStatus.Unknown;

        if (!RoundRestoreGateUtility.IsInPlay(status))
        {
            SetBackupWriting(!IsHoldingForRestore());
            return;
        }

        _logger.LogWarning($"Round {round} was not restored, holding the match for another vote");
        HoldForRestore(round);
    }

    private void ReturnOwedToHold()
    {
        if (_restore.FailedLoad() is int round)
        {
            ReturnToHold(round);
        }
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

        if (timeout.HoldRound is int round)
        {
            ReturnToHold(round);
            return;
        }

        SetBackupWriting(!IsHoldingForRestore());
    }

    public void TellWhyResumeWaits()
    {
        string? refusal = RoundRestoreGateUtility.ResumeRefusal(_restore.OwedRound);

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
        ReplayMap();

        return true;
    }

    private void ReplayMap()
    {
        SetBackupWriting(true);
        SendRestoreRoundToBackend(0);
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
            _restore.StartVote(recovering);

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
                        if (recovering)
                        {
                            _restore.Hold(round);
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
                        ReplayMap();
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

    public void SendRestoreRoundToBackend(int round)
    {
        _logger.LogInformation($"Restoring Round {round}");
        MatchManager? matchManager = _matchService.GetCurrentMatch();
        MatchData? match = matchManager?.GetMatchData();
        if (matchManager == null || match?.current_match_map_id == null)
        {
            return;
        }

        Guid mapId = matchManager.GetActiveMapId() ?? match.current_match_map_id.Value;

        _matchEvents.PublishGameEvent(
            "restoreRound",
            new Dictionary<string, object> { { "round", round }, { "match_map_id", mapId } }
        );
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

        BackupRound? backupRound = RoundRestoreGateUtility.LatestBackup(matchMap.rounds, round);

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

        File.WriteAllText(backupRoundFilePath, backupRound.backup_file);

        _restore.ReleaseHold();
        _restore.StartLoad();

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
                                round,
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
            matchMap != null
                && RoundRestoreGateUtility.LatestBackup(matchMap.rounds, round) != null,
            MatchUtility.Players().Count,
            _matchService.GetCurrentMatch()?.GetExpectedPlayerCount() ?? 10,
            match?.options.type ?? ""
        );
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
