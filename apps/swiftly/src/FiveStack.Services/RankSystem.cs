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
    private const int RankTypeCompetitive = 11;

    private const int MinWinsForVisibility = 10;

    private const float RevealAllInterval = 3.0f;

    private readonly ISwiftlyCore _core;
    private readonly ILogger<RankSystem> _logger;
    private readonly MatchService _matchService;

    private Dictionary<ulong, int>? _eloBySteamId;

    private readonly Dictionary<ulong, (int Ranking, int RankType, int Wins)> _lastNotified = new();

    private CancellationTokenSource? _revealAllTimer;

    public RankSystem(ISwiftlyCore core, ILogger<RankSystem> logger, MatchService matchService)
    {
        _core = core;
        _logger = logger;
        _matchService = matchService;
    }

    public void Start()
    {
        _core.Event.OnTick += OnTick;
        _revealAllTimer = _core.Scheduler.RepeatBySeconds(RevealAllInterval, SendRevealAll);
    }

    public void Stop()
    {
        _core.Event.OnTick -= OnTick;
        _revealAllTimer?.Cancel();
        _revealAllTimer = null;
    }

    public void OnMatchSetup(MatchData matchData)
    {
        _lastNotified.Clear();

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

        SendRevealAll();
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

    public void SendRevealAll()
    {
        if (_eloBySteamId == null)
        {
            return;
        }

        try
        {
            using var message = _core.NetMessage.Create<CCSUsrMsg_ServerRankRevealAll>();
            message.SendToAllPlayers();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RankSystem: failed to send reveal-all");
        }
    }

    private void OnTick()
    {
        if (_eloBySteamId == null)
        {
            return;
        }

        try
        {
            MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();
            if (matchData == null || !matchData.options.show_elo_ranks)
            {
                return;
            }

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

                if (!_eloBySteamId.TryGetValue(player.SteamID, out var elo))
                {
                    continue;
                }

                SetCompetitiveRank(player, elo);
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

    private void SetCompetitiveRank(IPlayer player, int elo)
    {
        CCSPlayerController controller = player.Controller;

        int rankValue = elo;
        int rankType = RankTypeCompetitive;
        int wins = MinWinsForVisibility;
        var intended = (rankValue, rankType, wins);

        if (
            _lastNotified.TryGetValue(player.SteamID, out var last)
            && last == intended
            && controller.CompetitiveRanking == rankValue
            && controller.CompetitiveRankType == (byte)rankType
            && controller.CompetitiveWins == wins
        )
        {
            return;
        }

        controller.CompetitiveRankType = (byte)rankType;
        controller.CompetitiveRanking = rankValue;
        controller.CompetitiveWins = wins;

        controller.CompetitiveRankTypeUpdated();
        controller.CompetitiveRankingUpdated();
        controller.CompetitiveWinsUpdated();

        _lastNotified[player.SteamID] = intended;
    }
}
