using System.Runtime.InteropServices;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Memory;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack;

// void CCSPlayerController::UpdateResourceData()
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void UpdateResourceDataDelegate(nint controller);

public class RankSystem
{
    private const int RankTypeCompetitive = 11;

    private const int MinWinsForVisibility = 10;

    // How often the tracked-player list is rebuilt. The pin runs inside the
    // engine's own update, so the roster sweep is kept off that path.
    private const float RosterRefreshInterval = 1.0f;

    private const string UpdateResourceDataSignature =
        "FiveStack_CCSPlayerController_UpdateResourceData";

    private readonly record struct TrackedPlayer(
        CCSPlayerController Controller,
        ulong SteamId,
        int Elo
    );

    private readonly ISwiftlyCore _core;
    private readonly ILogger<RankSystem> _logger;
    private readonly MatchService _matchService;

    private Dictionary<ulong, int>? _eloBySteamId;

    private Dictionary<nint, TrackedPlayer> _tracked = [];

    private CancellationTokenSource? _rosterTimer;

    private IUnmanagedFunction<UpdateResourceDataDelegate>? _updateResourceData;
    private Guid _hookId;

    private EventDelegates.OnTick? _tickFallback;

    public RankSystem(ISwiftlyCore core, ILogger<RankSystem> logger, MatchService matchService)
    {
        _core = core;
        _logger = logger;
        _matchService = matchService;
    }

    public void Start()
    {
        _rosterTimer = _core.Scheduler.RepeatBySeconds(RosterRefreshInterval, RefreshRoster);

        try
        {
            HookUpdateResourceData();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to hook UpdateResourceData, pinning elo ranks every tick instead"
            );
            _tickFallback = PinAll;
            _core.Event.OnTick += _tickFallback;
        }
    }

    public void Stop()
    {
        if (_updateResourceData != null && _hookId != Guid.Empty)
        {
            _updateResourceData.RemoveHook(_hookId);
        }
        _updateResourceData = null;
        _hookId = Guid.Empty;

        if (_tickFallback != null)
        {
            _core.Event.OnTick -= _tickFallback;
            _tickFallback = null;
        }

        _rosterTimer?.Cancel();
        _rosterTimer = null;
        _tracked = [];
    }

    private void HookUpdateResourceData()
    {
        GamedataSignature? signature = GamedataUtility.Load(
            _core.PluginPath,
            UpdateResourceDataSignature
        );

        nint? address =
            signature == null
                ? null
                : _core.Memory.GetAddressBySignature(signature.Library, signature.Pattern);

        if (address == null || address == nint.Zero)
        {
            throw new InvalidOperationException("UpdateResourceData signature not found");
        }

        _updateResourceData =
            _core.Memory.GetUnmanagedFunctionByAddress<UpdateResourceDataDelegate>(address.Value);
        _hookId = _updateResourceData.AddHook(next =>
            controller =>
            {
                next()(controller);
                OnResourceDataUpdated(controller);
            }
        );
    }

    // The engine resets the competitive rank fields here every 0.1s. Pinning right
    // after it, inside the same call, means the reset is never networked, so the
    // rank doesn't blink.
    private void OnResourceDataUpdated(nint controller)
    {
        if (!_tracked.TryGetValue(controller, out TrackedPlayer tracked))
        {
            return;
        }

        try
        {
            Pin(tracked);
        }
        catch (Exception ex)
        {
            Disable(ex, "RankSystem.OnResourceDataUpdated failed; disabling elo rank display");
        }
    }

    private void PinAll()
    {
        var tracked = _tracked;
        if (tracked.Count == 0)
        {
            return;
        }

        try
        {
            foreach (TrackedPlayer player in tracked.Values)
            {
                if (!player.Controller.IsValid)
                {
                    continue;
                }

                Pin(player);
            }
        }
        catch (Exception ex)
        {
            Disable(ex, "RankSystem.PinAll failed; disabling elo rank display");
        }
    }

    public void Refresh()
    {
        RefreshRoster();
        PinAll();
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

            var tracked = new Dictionary<nint, TrackedPlayer>();

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

                CCSPlayerController controller = player.Controller;
                tracked[controller.Address] = new TrackedPlayer(
                    controller,
                    controller.SteamID,
                    elo
                );
            }

            _tracked = tracked;
        }
        catch (Exception ex)
        {
            // This runs on a repeating timer, so a recurring failure would spam the
            // log and burn CPU every interval. Report it once and stay off.
            Disable(ex, "RankSystem.RefreshRoster failed; disabling elo rank display");
        }
    }

    private void Disable(Exception ex, string message)
    {
        _tracked = [];
        _eloBySteamId = null;
        _logger.LogError(ex, message);
    }

    private static void Pin(TrackedPlayer tracked)
    {
        CCSPlayerController controller = tracked.Controller;

        // The slot can change hands between roster refreshes.
        if (controller.SteamID != tracked.SteamId)
        {
            return;
        }

        // Reading first keeps the common case to three schema reads; the write plus
        // the three *Updated() network flushes only happen once the engine reverts.
        if (
            controller.CompetitiveRanking == tracked.Elo
            && controller.CompetitiveRankType == (byte)RankTypeCompetitive
            && controller.CompetitiveWins == MinWinsForVisibility
        )
        {
            return;
        }

        controller.CompetitiveRankType = (byte)RankTypeCompetitive;
        controller.CompetitiveRanking = tracked.Elo;
        controller.CompetitiveWins = MinWinsForVisibility;

        controller.CompetitiveRankTypeUpdated();
        controller.CompetitiveRankingUpdated();
        controller.CompetitiveWinsUpdated();
    }
}
