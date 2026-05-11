using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    CsTeam roundWinner;
    eWinReason? reason;
    private bool _suppressNextCapture = false;

    private class RoundResultSnapshot
    {
        public int Round { get; set; }
        public System.Guid MatchMapId { get; set; }
        public System.DateTime CapturedAt { get; set; }
        public int LiveTScore { get; set; }
        public int LiveCtScore { get; set; }
        public string Lineup1Money { get; set; } = "0";
        public string Lineup2Money { get; set; } = "0";
        public int Lineup1Timeouts { get; set; }
        public int Lineup2Timeouts { get; set; }
        public CsTeam Winner { get; set; }
        public eWinReason WinReason { get; set; }
    }

    private RoundResultSnapshot? _pendingRoundResult;

    [GameEventHandler]
    public HookResult OnRoundOfficiallyEnded(EventRoundOfficiallyEnded @event, GameEventInfo info)
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

        if (match.IsKnife() || !match.IsInProgress())
        {
            _logger.LogInformation(
                "OnRoundOfficiallyEnded skipping capture: isKnife={IsKnife} isInProgress={IsInProgress}",
                match.IsKnife(),
                match.IsInProgress()
            );
            return HookResult.Continue;
        }

        if (_gameBackupRounds.IsResettingRound())
        {
            _logger.LogInformation("OnRoundOfficiallyEnded skipping capture: restoring round");
            return HookResult.Continue;
        }

        if (_suppressNextCapture)
        {
            _logger.LogInformation(
                "OnRoundOfficiallyEnded skipping capture: suppressed by prior non-round RoundEnd (e.g. Game_Commencing)"
            );
            _suppressNextCapture = false;
            return HookResult.Continue;
        }

        CaptureRoundResult(match, matchData, currentMap);

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (_gameBackupRounds.IsResettingRound())
        {
            _logger.LogInformation(
                "OnRoundEnd ignored (restoring round): message={Message}",
                @event.Message
            );
            return HookResult.Continue;
        }

        if (@event.Message == "#SFUI_Notice_Game_Commencing")
        {
            _logger.LogInformation(
                "OnRoundEnd ignored (Game_Commencing): not a real round, suppressing next capture"
            );
            _suppressNextCapture = true;
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

        roundWinner = TeamUtility.TeamNumToCSTeam(@event.Winner);

        if (match == null || matchData == null || currentMap == null)
        {
            return HookResult.Continue;
        }

        if (match.IsKnife())
        {
            match.knifeSystem.SetWinningTeam(TeamUtility.TeamNumToCSTeam(@event.Winner));
        }

        int liveTScoreAtEnd = 0;
        int liveCtScoreAtEnd = 0;
        foreach (var team in MatchUtility.Teams())
        {
            if (team.TeamNum == (int)CsTeam.Terrorist) liveTScoreAtEnd = team.Score;
            else if (team.TeamNum == (int)CsTeam.CounterTerrorist) liveCtScoreAtEnd = team.Score;
        }
        _logger.LogInformation(
            "OnRoundEnd totalRoundsPlayed={TotalRoundsPlayed} winner={Winner} reason={Reason} live_t={LiveT} live_ct={LiveCt} isKnife={IsKnife}",
            _gameServer.GetTotalRoundsPlayed(),
            roundWinner,
            reason,
            liveTScoreAtEnd,
            liveCtScoreAtEnd,
            match.IsKnife()
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
            if (team.TeamNum == (int)CsTeam.Terrorist) liveT = team.Score;
            else if (team.TeamNum == (int)CsTeam.CounterTerrorist) liveCt = team.Score;
        }

        (int l1Timeouts, int l2Timeouts) = _timeoutSystem.GetLineupTimeouts();

        int currentSideIndex = System.Math.Max(0, totalRoundsPlayed - 1);
        string l1Money = TeamUtility
            .GetTeamMoney(matchData, currentMap, matchData.lineup_1_id, currentSideIndex)
            .ToString();
        string l2Money = TeamUtility
            .GetTeamMoney(matchData, currentMap, matchData.lineup_2_id, currentSideIndex)
            .ToString();

        _pendingRoundResult = new RoundResultSnapshot
        {
            Round = totalRoundsPlayed,
            MatchMapId = match.GetActiveMapId() ?? currentMap.id,
            CapturedAt = System.DateTime.Now,
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
            "CaptureRoundResult round={Round} match_map={MatchMapId} live_t={LiveT} live_ct={LiveCt} winner={Winner} reason={Reason}",
            totalRoundsPlayed,
            _pendingRoundResult.MatchMapId,
            liveT,
            liveCt,
            roundWinner,
            reason
        );
    }

    public void PublishPendingRound(bool SendBackupRound)
    {
        RoundResultSnapshot? snap = _pendingRoundResult;
        if (snap == null)
        {
            _logger.LogInformation(
                "PublishPendingRound skipped: no pending round (sendBackup={SendBackup})",
                SendBackupRound
            );
            return;
        }

        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();
        MatchMap? currentMap = match?.GetCurrentMap();

        if (match == null || matchData == null || currentMap == null)
        {
            _logger.LogWarning(
                "PublishPendingRound skipped: null state (match={MatchNull} matchData={MatchDataNull} currentMap={CurrentMapNull})",
                match == null,
                matchData == null,
                currentMap == null
            );
            return;
        }

        int displaySideIndex = System.Math.Max(0, snap.Round - 1);

        CsTeam l1Side = TeamUtility.GetLineupSide(
            matchData,
            currentMap,
            matchData.lineup_1_id,
            displaySideIndex
        );
        CsTeam l2Side = TeamUtility.GetLineupSide(
            matchData,
            currentMap,
            matchData.lineup_2_id,
            displaySideIndex
        );

        int l1Score = ScoreForSide(snap, l1Side);
        int l2Score = ScoreForSide(snap, l2Side);

        string backupFile =
            SendBackupRound
                ? (_gameBackupRounds.GetBackupRoundFile(_gameServer.GetTotalRoundsPlayed()) ?? "")
                : "";

        _logger.LogInformation(
            "PublishPendingRound round={Round} captured_t={CapturedT} captured_ct={CapturedCt} "
                + "l1_side={L1Side} l1_score={L1Score} l2_side={L2Side} l2_score={L2Score} "
                + "winning_side={WinningSide} reason={Reason} backup_file_present={HasBackup}",
            snap.Round,
            snap.LiveTScore,
            snap.LiveCtScore,
            l1Side,
            l1Score,
            l2Side,
            l2Score,
            snap.Winner,
            snap.WinReason,
            !string.IsNullOrEmpty(backupFile)
        );

        _matchEvents.PublishGameEvent(
            "score",
            new System.Collections.Generic.Dictionary<string, object>
            {
                { "time", snap.CapturedAt },
                { "match_map_id", snap.MatchMapId },
                { "round", snap.Round },
                { "lineup_1_score", l1Score },
                { "lineup_1_money", snap.Lineup1Money },
                { "lineup_1_timeouts_available", $"{snap.Lineup1Timeouts}" },
                { "lineup_2_score", $"{l2Score}" },
                { "lineup_2_money", snap.Lineup2Money },
                { "lineup_2_timeouts_available", $"{snap.Lineup2Timeouts}" },
                { "lineup_1_side", $"{TeamUtility.CSTeamToString(l1Side)}" },
                { "lineup_2_side", $"{TeamUtility.CSTeamToString(l2Side)}" },
                { "winning_side", $"{TeamUtility.CSTeamToString(snap.Winner)}" },
                { "winning_reason", $"{snap.WinReason}" },
                { "backup_file", backupFile },
            }
        );

        _pendingRoundResult = null;
    }

    private static int ScoreForSide(RoundResultSnapshot snap, CsTeam side)
    {
        if (side == CsTeam.Terrorist) return snap.LiveTScore;
        if (side == CsTeam.CounterTerrorist) return snap.LiveCtScore;
        return 0;
    }
}
