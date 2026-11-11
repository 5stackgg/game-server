using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.UserMessages;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class RankSystem
{
    private const int MinWinsForVisibility = 10;

    private const int RankTypeCompetitive = 11;

    // How often the tracked-player list is rebuilt. The pin itself has to run every
    // tick (see OnTick), so the expensive roster sweep is kept off the tick path.
    public const float RosterRefreshInterval = 1.0f;

    private const string RankRevealAllMessage = "CCSUsrMsg_ServerRankRevealAll";

    private static readonly string[] RankFields =
    [
        "m_iCompetitiveRankType",
        "m_iCompetitiveRanking",
        "m_iCompetitiveWins",
        "m_iCompetitiveRankingPredicted_Win",
        "m_iCompetitiveRankingPredicted_Loss",
        "m_iCompetitiveRankingPredicted_Tie",
    ];

    private readonly ILogger<RankSystem> _logger;
    private readonly MatchService _matchService;

    private Dictionary<ulong, EloRank>? _eloBySteamId;

    private (CCSPlayerController Player, EloRank Rank)[] _tracked = [];

    public RankSystem(ILogger<RankSystem> logger, MatchService matchService)
    {
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

        // Touching the competitive rank fields throws when this is enabled, so stay
        // off entirely rather than throwing out of every interval callback.
        if (IsFollowCS2ServerGuidelinesBlocking())
        {
            _eloBySteamId = null;
            _logger.LogWarning(
                "Elo ranks will not render: FollowCS2ServerGuidelines is true in CounterStrikeSharp core.json. Set it to false (or SHOW_ELO_RANKS=true on the host and restart so setup.sh can patch it)."
            );
            return;
        }

        _eloBySteamId = BuildEloLookup(matchData);

        _logger.LogInformation(
            $"Elo rank display enabled for match {matchData.id} (mode={matchData.options.type}, {_eloBySteamId.Count} player(s) with elo)"
        );

        Refresh();
    }

    public void Refresh()
    {
        RefreshRoster();
        OnTick();
        SendRevealAll();
    }

    // The engine reverts the competitive rank netvars almost immediately, so the
    // value has to be re-pinned every tick to stay visible. Pinning on an interval
    // instead makes the rank blink on for a frame once per interval.
    public void OnTick()
    {
        var tracked = _tracked;
        if (tracked.Length == 0)
        {
            return;
        }

        try
        {
            foreach (var (player, rank) in tracked)
            {
                if (!player.IsValid)
                {
                    continue;
                }

                SetCompetitiveRank(player, rank);
            }
        }
        catch (Exception ex)
        {
            _tracked = [];
            _eloBySteamId = null;
            _logger.LogError(ex, "RankSystem.OnTick failed; disabling elo rank display");
        }
    }

    private static Dictionary<ulong, EloRank> BuildEloLookup(MatchData matchData)
    {
        var lookup = new Dictionary<ulong, EloRank>();
        foreach (
            var member in matchData.lineup_1.lineup_players.Concat(
                matchData.lineup_2.lineup_players
            )
        )
        {
            if (EloRank.From(member) is not EloRank rank)
            {
                continue;
            }
            if (!ulong.TryParse(member.steam_id, out var sid))
            {
                continue;
            }
            lookup[sid] = rank;
        }
        return lookup;
    }

    private static bool IsFollowCS2ServerGuidelinesBlocking()
    {
        string configPath = Path.Join(
            Server.GameDirectory,
            "csgo",
            "addons",
            "counterstrikesharp",
            "configs",
            "core.json"
        );

        if (!File.Exists(configPath))
        {
            return false;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(configPath));
            return doc.RootElement.TryGetProperty("FollowCS2ServerGuidelines", out var prop)
                && prop.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void SendRevealAll()
    {
        if (_eloBySteamId == null)
        {
            return;
        }

        try
        {
            using UserMessage message = UserMessage.FromPartialName(RankRevealAllMessage);
            message.Recipients.AddAllPlayers();
            message.Send();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RankSystem: failed to send {Message}", RankRevealAllMessage);
        }
    }

    public void RefreshRoster()
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

            var tracked = new List<(CCSPlayerController, EloRank)>();

            foreach (var player in MatchUtility.Players())
            {
                if (!_eloBySteamId.TryGetValue(player.SteamID, out var rank))
                {
                    continue;
                }

                tracked.Add((player, rank));
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

    private static void SetCompetitiveRank(CCSPlayerController player, EloRank rank)
    {
        // Reading first keeps the common case to schema reads; the writes and their
        // SetStateChanged network flushes only happen once the engine reverts.
        if (
            player.CompetitiveRanking == rank.Elo
            && player.CompetitiveRankType == (sbyte)RankTypeCompetitive
            && player.CompetitiveWins == MinWinsForVisibility
            && player.CompetitiveRankingPredicted_Win == rank.Win
            && player.CompetitiveRankingPredicted_Loss == rank.Loss
            && player.CompetitiveRankingPredicted_Tie == rank.Tie
        )
        {
            return;
        }

        player.CompetitiveRankType = (sbyte)RankTypeCompetitive;
        player.CompetitiveRanking = rank.Elo;
        player.CompetitiveWins = MinWinsForVisibility;
        player.CompetitiveRankingPredicted_Win = rank.Win;
        player.CompetitiveRankingPredicted_Loss = rank.Loss;
        player.CompetitiveRankingPredicted_Tie = rank.Tie;

        foreach (string field in RankFields)
        {
            CounterStrikeSharp.API.Utilities.SetStateChanged(player, "CCSPlayerController", field);
        }
    }
}
