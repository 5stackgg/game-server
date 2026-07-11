using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace FiveStack;

public partial class FiveStackPlugin
{
    Team roundWinner;
    eWinReason? reason;

    [GameEventHandler(HookMode.Post)]
    public HookResult OnRoundOfficiallyEnded(EventRoundOfficiallyEnded @event)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();
        MatchMap? currentMap = match?.GetCurrentMap();

        if (match == null || currentMap == null || matchData?.current_match_map_id == null)
        {
            return HookResult.Continue;
        }

        if (match.IsMapFinished())
        {
            return HookResult.Continue;
        }

        if (match.isOverTime())
        {
            match.UpdateMapStatus(eMapStatus.Overtime);
        }

        if (_gameBackupRounds.IsResettingRound())
        {
            _logger.LogInformation("OnRoundOfficiallyEnded skipping capture: restoring round");
            return HookResult.Continue;
        }

        if (match.IsKnife())
        {
            _logger.LogInformation("OnRoundOfficiallyEnded skipping capture: knife round");
            return HookResult.Continue;
        }

        CaptureRoundResult(match, matchData, currentMap);

        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnRoundEnd(EventRoundEnd @event)
    {
        if (_gameBackupRounds.IsResettingRound())
        {
            _logger.LogInformation(
                $"OnRoundEnd ignored (restoring round): message={@event.Message}"
            );
            return HookResult.Continue;
        }

        if (@event.Message == "#SFUI_Notice_Game_Commencing")
        {
            _logger.LogInformation("OnRoundEnd ignored (Game_Commencing): not a real round end");
            return HookResult.Continue;
        }

        switch (@event.Message)
        {
            case "#SFUI_Notice_Terrorists_Win":
                reason = eWinReason.TerroristsWin;
                break;
            case "#SFUI_Notice_CTs_Win":
                reason = eWinReason.CTsWin;
                break;
            case "#SFUI_Notice_Target_Bombed":
                reason = eWinReason.BombExploded;
                break;
            case "#SFUI_Notice_Target_Saved":
                reason = eWinReason.TimeRanOut;
                break;
            case "#SFUI_Notice_Bomb_Defused":
                reason = eWinReason.BombDefused;
                break;
            default:
                _logger.LogWarning($"Unknown round end reason: {@event.Message}");
                reason = eWinReason.Unknown;
                break;
        }

        MatchManager? match = _matchService.GetCurrentMatch();
        MatchMap? currentMap = match?.GetCurrentMap();
        MatchData? matchData = match?.GetMatchData();

        roundWinner = TeamUtility.TeamNumToTeam(@event.Winner);

        if (match == null || matchData == null || currentMap == null)
        {
            return HookResult.Continue;
        }

        if (match.IsKnife())
        {
            match.knifeSystem.SetWinningTeam(TeamUtility.TeamNumToTeam(@event.Winner));
        }

        int liveTScoreAtEnd = 0;
        int liveCtScoreAtEnd = 0;
        foreach (var team in MatchUtility.Teams())
        {
            if (team.TeamNum == (int)Team.T)
            {
                liveTScoreAtEnd = team.Score;
            }
            else if (team.TeamNum == (int)Team.CT)
            {
                liveCtScoreAtEnd = team.Score;
            }
        }
        _logger.LogInformation(
            $"OnRoundEnd totalRoundsPlayed={_gameServer.GetTotalRoundsPlayed()} winner={roundWinner} reason={reason} live_t={liveTScoreAtEnd} live_ct={liveCtScoreAtEnd} isKnife={match.IsKnife()}"
        );

        return HookResult.Continue;
    }

    private void CaptureRoundResult(MatchManager match, MatchData matchData, MatchMap currentMap)
    {
        int totalRoundsPlayed = _gameServer.GetTotalRoundsPlayed();

        int liveT = 0;
        int liveCt = 0;
        foreach (var team in MatchUtility.Teams())
        {
            if (team.TeamNum == (int)Team.T)
            {
                liveT = team.Score;
            }
            else if (team.TeamNum == (int)Team.CT)
            {
                liveCt = team.Score;
            }
        }

        (int l1Timeouts, int l2Timeouts) = match.timeoutSystem.GetLineupTimeouts();

        int currentSideIndex = Math.Max(0, totalRoundsPlayed - 1);
        string l1Money = TeamUtility
            .GetTeamMoney(matchData, currentMap, matchData.lineup_1_id, currentSideIndex)
            .ToString();
        string l2Money = TeamUtility
            .GetTeamMoney(matchData, currentMap, matchData.lineup_2_id, currentSideIndex)
            .ToString();

        _matchEvents.PendingRoundResult = new MatchEvents.RoundResultSnapshot
        {
            Round = totalRoundsPlayed,
            MatchMapId = match.GetActiveMapId() ?? currentMap.id,
            CapturedAt = DateTime.Now,
            LiveTScore = liveT,
            LiveCtScore = liveCt,
            Lineup1Money = l1Money,
            Lineup2Money = l2Money,
            Lineup1Timeouts = l1Timeouts,
            Lineup2Timeouts = l2Timeouts,
            Winner = roundWinner,
            WinReason = reason ?? eWinReason.Unknown,
        };

        _logger.LogInformation(
            $"CaptureRoundResult round={totalRoundsPlayed} match_map={_matchEvents.PendingRoundResult.MatchMapId} live_t={liveT} live_ct={liveCt} winner={roundWinner} reason={reason}"
        );
    }

    public void PublishPendingRound(bool SendBackupRound)
    {
        MatchEvents.RoundResultSnapshot? snap = _matchEvents.PendingRoundResult;
        if (snap == null)
        {
            _logger.LogInformation(
                $"PublishPendingRound skipped: no pending round (sendBackup={SendBackupRound})"
            );
            return;
        }

        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();
        MatchMap? currentMap = match?.GetCurrentMap();

        if (match == null || matchData == null || currentMap == null)
        {
            _logger.LogWarning(
                $"PublishPendingRound skipped: null state (match={match == null} matchData={matchData == null} currentMap={currentMap == null})"
            );
            return;
        }

        int displaySideIndex = Math.Max(0, snap.Round - 1);

        Team l1Side = TeamUtility.GetLineupSide(
            matchData,
            currentMap,
            matchData.lineup_1_id,
            displaySideIndex
        );
        Team l2Side = TeamUtility.GetLineupSide(
            matchData,
            currentMap,
            matchData.lineup_2_id,
            displaySideIndex
        );

        int l1Score = ScoreForSide(snap, l1Side);
        int l2Score = ScoreForSide(snap, l2Side);

        int backupRoundNumber = _gameServer.GetTotalRoundsPlayed();
        string backupFile = SendBackupRound
            ? (_gameBackupRounds.GetBackupRoundFile(backupRoundNumber) ?? "")
            : "";

        _logger.LogInformation(
            $"PublishPendingRound published_round={snap.Round} backup_lookup_round={backupRoundNumber} sendBackup={SendBackupRound} captured_t={snap.LiveTScore} captured_ct={snap.LiveCtScore} l1_side={l1Side} l1_score={l1Score} l2_side={l2Side} l2_score={l2Score} winning_side={snap.Winner} reason={snap.WinReason} backup_file_present={!string.IsNullOrEmpty(backupFile)}"
        );

        _matchEvents.PublishGameEvent(
            "score",
            new Dictionary<string, object>
            {
                { "time", snap.CapturedAt },
                { "match_map_id", snap.MatchMapId },
                { "round", snap.Round },
                { "lineup_1_score", l1Score },
                { "lineup_1_money", snap.Lineup1Money },
                { "lineup_1_timeouts_available", $"{snap.Lineup1Timeouts}" },
                { "lineup_2_score", l2Score },
                { "lineup_2_money", snap.Lineup2Money },
                { "lineup_2_timeouts_available", $"{snap.Lineup2Timeouts}" },
                { "lineup_1_side", $"{TeamUtility.TeamToString(l1Side)}" },
                { "lineup_2_side", $"{TeamUtility.TeamToString(l2Side)}" },
                { "winning_side", $"{TeamUtility.TeamToString(snap.Winner)}" },
                { "winning_reason", $"{snap.WinReason}" },
                { "backup_file", backupFile },
            }
        );

        _matchEvents.PendingRoundResult = null;
    }

    private static int ScoreForSide(MatchEvents.RoundResultSnapshot snap, Team side)
    {
        if (side == Team.T)
        {
            return snap.LiveTScore;
        }
        if (side == Team.CT)
        {
            return snap.LiveCtScore;
        }
        return 0;
    }
}
