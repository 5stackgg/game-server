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

    // How often the tracked-player list is rebuilt. The pin itself has to run every
    // tick (see OnTick), so the expensive roster sweep is kept off the tick path.
    private const float RosterRefreshInterval = 1.0f;

    private readonly ISwiftlyCore _core;
    private readonly ILogger<RankSystem> _logger;
    private readonly MatchService _matchService;

    private Dictionary<ulong, int>? _eloBySteamId;

    private (IPlayer Player, int Elo)[] _tracked = [];

    private CancellationTokenSource? _rosterTimer;

    public RankSystem(ISwiftlyCore core, ILogger<RankSystem> logger, MatchService matchService)
    {
        _core = core;
        _logger = logger;
        _matchService = matchService;
    }

    public void Start()
    {
        _rosterTimer = _core.Scheduler.RepeatBySeconds(RosterRefreshInterval, RefreshRoster);
        _core.Event.OnTick += OnTick;
    }

    public void Stop()
    {
        _core.Event.OnTick -= OnTick;
        _rosterTimer?.Cancel();
        _rosterTimer = null;
        _tracked = [];
    }

    // The engine reverts the competitive rank netvars almost immediately, so the
    // value has to be re-pinned every tick to stay visible. Pinning on an interval
    // instead makes the rank blink on for a frame once per interval.
    private void OnTick()
    {
        var tracked = _tracked;
        if (tracked.Length == 0)
        {
            return;
        }

        try
        {
            foreach (var (player, elo) in tracked)
            {
                if (!player.IsValid || player.Controller == null)
                {
                    continue;
                }

                SetCompetitiveRank(player, elo);
            }
        }
        catch (Exception ex)
        {
            _tracked = [];
            _eloBySteamId = null;
            _logger.LogError(ex, "RankSystem.OnTick failed; disabling elo rank display");
        }
    }

    public void Refresh()
    {
        RefreshRoster();
        OnTick();
        SendRevealAll();
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

        Refresh();
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

    private void RefreshRoster()
    {
        if (_eloBySteamId == null)
        {
            _tracked = [];
            return;
        }

        try
        {
            MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();
            if (matchData == null || !matchData.options.show_elo_ranks)
            {
                _tracked = [];
                return;
            }

            var tracked = new List<(IPlayer, int)>();

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

                tracked.Add((player, elo));
            }

            _tracked = tracked.ToArray();
        }
        catch (Exception ex)
        {
            // This runs on a repeating timer, so a recurring failure would spam the
            // log and burn CPU every interval. Report it once and stay off.
            _tracked = [];
            _eloBySteamId = null;
            _logger.LogError(ex, "RankSystem.RefreshRoster failed; disabling elo rank display");
        }
    }

    private static void SetCompetitiveRank(IPlayer player, int elo)
    {
        CCSPlayerController controller = player.Controller;

        // Reading first keeps the common case to three schema reads; the write plus
        // the three *Updated() network flushes only happen once the engine reverts.
        if (
            controller.CompetitiveRanking == elo
            && controller.CompetitiveRankType == (byte)RankTypeCompetitive
            && controller.CompetitiveWins == MinWinsForVisibility
        )
        {
            return;
        }

        controller.CompetitiveRankType = (byte)RankTypeCompetitive;
        controller.CompetitiveRanking = elo;
        controller.CompetitiveWins = MinWinsForVisibility;

        controller.CompetitiveRankTypeUpdated();
        controller.CompetitiveRankingUpdated();
        controller.CompetitiveWinsUpdated();
    }
}
