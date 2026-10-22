using CounterStrikeSharp.API;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class IdleMapReload
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    private readonly MatchService _matchService;
    private readonly ILogger<IdleMapReload> _logger;
    private Timer? _timer;

    public IdleMapReload(ILogger<IdleMapReload> logger, MatchService matchService)
    {
        _logger = logger;
        _matchService = matchService;
    }

    // Queued onto a world update by a thread timer: an empty server
    // hibernates, and its frames and game timers stop while it does.
    public void Start()
    {
        _timer = new Timer(_ => Server.NextWorldUpdate(Check), null, CheckEvery, CheckEvery);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Check()
    {
        float curTime = Server.CurrentTime;

        if (
            !IdleMapReloadUtility.ShouldReload(
                curTime,
                MatchUtility.Players().Count,
                _matchService.GetCurrentMatch() != null
            )
        )
        {
            return;
        }

        string command = IdleMapReloadUtility.ReloadCommand(
            Server.MapName,
            _matchService.GetWorkshopID()
        );

        _logger.LogInformation(
            $"empty server at curtime {curTime:0}s (hibernating: {HibernationUtility.IsHibernating}), reloading the map: {command}"
        );
        Server.ExecuteCommand(command);
    }
}
