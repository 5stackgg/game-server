using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

public class RankSystem
{
    private const int CompetitiveWinsForVisibility = 10;

    private const int RankTypeCompetitive = 11;

    private readonly ISwiftlyCore _core;
    private readonly ILogger<RankSystem> _logger;
    private readonly MatchService _matchService;

    private Dictionary<ulong, int>? _eloBySteamId;

    private const int TickLogInterval = 640;

    private const string RankRevealAllMessage = "CCSUsrMsg_ServerRankRevealAll";

    private const int RevealWhileOpenTickInterval = 16;

    private const ulong ScoreboardButton = 1UL << 33;

    private readonly Dictionary<int, bool> _scoreboardOpen = new();

    private long _tickCount;
    private long _ticksApplied;
    private int _lastAppliedPlayerCount;

    public RankSystem(ISwiftlyCore core, ILogger<RankSystem> logger, MatchService matchService)
    {
        _core = core;
        _logger = logger;
        _matchService = matchService;
    }

    public void OnMatchSetup(MatchData matchData)
    {
        if (!matchData.options.show_elo_ranks)
        {
            _eloBySteamId = null;
            _logger.LogInformation(
                $"Elo rank display disabled for match {matchData.id} (show_elo_ranks=false)"
            );
            return;
        }

        _eloBySteamId = BuildEloLookup(matchData);

        _logger.LogInformation(
            $"Elo rank display enabled for match {matchData.id} (mode={matchData.options.type}, {_eloBySteamId.Count} player(s) with elo)"
        );
    }

    private static Dictionary<ulong, int> BuildEloLookup(MatchData matchData)
    {
        var lookup = new Dictionary<ulong, int>();
        foreach (
            var member in matchData.lineup_1.lineup_players.Concat(
                matchData.lineup_2.lineup_players
            )
        )
        {
            if (!member.elo.HasValue)
            {
                continue;
            }
            if (!ulong.TryParse(member.steam_id, out var sid))
            {
                continue;
            }
            lookup[sid] = member.elo.Value;
        }
        return lookup;
    }

    public void OnTick()
    {
        try
        {
            MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();
            if (matchData == null || !matchData.options.show_elo_ranks || _eloBySteamId == null)
            {
                return;
            }

            int applied = 0;

            foreach (IPlayer player in _core.PlayerManager.GetAllPlayers())
            {
                if (
                    player == null
                    || !player.IsValid
                    || player.IsFakeClient
                    || player.Controller == null
                    || player.Controller.PlayerName == "SourceTV"
                )
                {
                    continue;
                }

                RevealRanksToScoreboardViewer(player);

                if (!_eloBySteamId.TryGetValue(player.SteamID, out var elo))
                {
                    continue;
                }

                CCSPlayerController controller = player.Controller;

                if (
                    controller.CompetitiveRanking == elo
                    && controller.CompetitiveRankType == (byte)RankTypeCompetitive
                    && controller.CompetitiveWins == CompetitiveWinsForVisibility
                )
                {
                    continue;
                }

                controller.CompetitiveRanking = elo;
                controller.CompetitiveRankType = (byte)RankTypeCompetitive;
                controller.CompetitiveWins = CompetitiveWinsForVisibility;

                controller.CompetitiveRankingUpdated();
                controller.CompetitiveRankTypeUpdated();
                controller.CompetitiveWinsUpdated();

                applied++;
            }

            _tickCount++;
            if (applied > 0)
            {
                _ticksApplied++;
                _lastAppliedPlayerCount = applied;
            }

            if (_tickCount % TickLogInterval == 0)
            {
                _logger.LogInformation(
                    $"RankSystem.OnTick heartbeat: ticks={_tickCount} applied_ticks={_ticksApplied} last_player_count={_lastAppliedPlayerCount}"
                );
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "RankSystem.OnTick: invalid state during iteration");
        }
        catch (NullReferenceException ex)
        {
            _logger.LogError(ex, "RankSystem.OnTick: unexpected null from external API");
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "RankSystem.OnTick: bad argument from upstream data");
        }
    }

    private void RevealRanksToScoreboardViewer(IPlayer target)
    {
        CCSPlayerController player = target.Controller;

        CPlayer_MovementServices? movementServices = player.Pawn.Value?.MovementServices;
        if (movementServices == null || movementServices.Buttons.ButtonStates.ElementCount == 0)
        {
            return;
        }

        bool isOpen =
            (movementServices.Buttons.ButtonStates[0] & ScoreboardButton) != 0;

        _scoreboardOpen.TryGetValue(target.Slot, out bool wasOpen);
        _scoreboardOpen[target.Slot] = isOpen;

        if (!isOpen)
        {
            return;
        }

        bool justOpened = !wasOpen;
        if (!justOpened && _tickCount % RevealWhileOpenTickInterval != 0)
        {
            return;
        }

        try
        {
            using var message = _core.NetMessage.Create<CCSUsrMsg_ServerRankRevealAll>();
            message.SendToPlayer(target.Slot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RankSystem: failed to send {Message}", RankRevealAllMessage);
        }
    }
}
