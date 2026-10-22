using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;

namespace FiveStack;

public class IdleMapReload
{
    private const long CheckEveryMs = 60_000;

    private readonly ISwiftlyCore _core;
    private readonly MatchService _matchService;
    private readonly ILogger<IdleMapReload> _logger;
    private EventDelegates.OnWorldUpdate? _handler;
    private long _lastCheckMs;

    public IdleMapReload(
        ISwiftlyCore core,
        ILogger<IdleMapReload> logger,
        MatchService matchService
    )
    {
        _core = core;
        _logger = logger;
        _matchService = matchService;
    }

    // On world updates, not a timer: an empty server hibernates, and its
    // timers stop while it does.
    public void Start()
    {
        _lastCheckMs = Environment.TickCount64;
        _handler = Check;
        _core.Event.OnWorldUpdate += _handler;
    }

    public void Stop()
    {
        if (_handler != null)
        {
            _core.Event.OnWorldUpdate -= _handler;
            _handler = null;
        }
    }

    private void Check()
    {
        long nowMs = Environment.TickCount64;

        if (nowMs - _lastCheckMs < CheckEveryMs)
        {
            return;
        }

        _lastCheckMs = nowMs;

        float curTime = _core.Engine.GlobalVars.CurrentTime;

        // Every client counts, still loading or not: SwiftlyS2 lists a player
        // from the moment the engine accepts the connection, and only bots and
        // CSTV are fake clients.
        int players = _core.PlayerManager.GetAllPlayers().Count(player => !player.IsFakeClient);

        if (
            !IdleMapReloadUtility.ShouldReload(
                curTime,
                players,
                _matchService.GetCurrentMatch() != null
            )
        )
        {
            return;
        }

        string command = IdleMapReloadUtility.ReloadCommand(
            _core.Engine.GlobalVars.MapName.ToString(),
            _matchService.GetWorkshopID()
        );

        _logger.LogInformation(
            $"empty server at curtime {curTime:0}s (hibernating: {HibernationUtility.IsHibernating}), reloading the map: {command}"
        );
        _core.Engine.ExecuteCommand(command);
    }
}
