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

    public const float RevealAllInterval = 3.0f;

    // Competitive rank fields only need periodic re-pinning; sweeping every tick
    // is needless main-thread work (native entity enumeration + allocations).
    private const float RankApplyInterval = 1.0f;

    private const string RankRevealAllMessage = "CCSUsrMsg_ServerRankRevealAll";

    private readonly ILogger<RankSystem> _logger;
    private readonly MatchService _matchService;

    private Dictionary<ulong, int>? _eloBySteamId;

    private float _nextRankApply;

    private readonly Dictionary<ulong, (int Ranking, int RankType, int Wins)> _lastNotified = new();

    public RankSystem(ILogger<RankSystem> logger, MatchService matchService)
    {
        _logger = logger;
        _matchService = matchService;
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

        if (IsFollowCS2ServerGuidelinesBlocking())
        {
            _logger.LogWarning(
                "Elo ranks will not render: FollowCS2ServerGuidelines is true in CounterStrikeSharp core.json. Set it to false (or SHOW_ELO_RANKS=true on the host and restart so setup.sh can patch it)."
            );
        }

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

    public void OnTick()
    {
        if (_eloBySteamId == null)
        {
            return;
        }

        if (Server.CurrentTime < _nextRankApply)
        {
            return;
        }
        _nextRankApply = Server.CurrentTime + RankApplyInterval;

        try
        {
            MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();
            if (matchData == null || !matchData.options.show_elo_ranks)
            {
                return;
            }

            foreach (var player in MatchUtility.Players())
            {
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

    private void SetCompetitiveRank(CCSPlayerController player, int elo)
    {
        int rankValue = elo;
        int rankType = RankTypeCompetitive;
        int wins = MinWinsForVisibility;
        var intended = (rankValue, rankType, wins);

        if (
            _lastNotified.TryGetValue(player.SteamID, out var last)
            && last == intended
            && player.CompetitiveRanking == rankValue
            && player.CompetitiveRankType == (sbyte)rankType
            && player.CompetitiveWins == wins
        )
        {
            return;
        }

        player.CompetitiveRankType = (sbyte)rankType;
        player.CompetitiveRanking = rankValue;
        player.CompetitiveWins = wins;

        CounterStrikeSharp.API.Utilities.SetStateChanged(
            player,
            "CCSPlayerController",
            "m_iCompetitiveRankType"
        );
        CounterStrikeSharp.API.Utilities.SetStateChanged(
            player,
            "CCSPlayerController",
            "m_iCompetitiveRanking"
        );
        CounterStrikeSharp.API.Utilities.SetStateChanged(
            player,
            "CCSPlayerController",
            "m_iCompetitiveWins"
        );

        _lastNotified[player.SteamID] = intended;
    }
}
