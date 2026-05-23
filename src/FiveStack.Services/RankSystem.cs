using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class RankSystem
{
    private const int CompetitiveWinsForVisibility = 10;

    private const int RankTypeCompetitive = 11;

    private readonly ILogger<RankSystem> _logger;
    private readonly MatchService _matchService;

    private Dictionary<ulong, int>? _eloBySteamId;

    private const int TickLogInterval = 640;
    private long _tickCount;
    private long _ticksApplied;
    private int _lastAppliedPlayerCount;

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

            foreach (var player in MatchUtility.Players())
            {
                if (!_eloBySteamId.TryGetValue(player.SteamID, out var elo))
                {
                    continue;
                }

                player.CompetitiveRanking = elo;
                player.CompetitiveRankType = (sbyte)RankTypeCompetitive;
                player.CompetitiveWins = CompetitiveWinsForVisibility;

                CounterStrikeSharp.API.Utilities.SetStateChanged(
                    player,
                    "CCSPlayerController",
                    "m_iCompetitiveRankType"
                );

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
}
