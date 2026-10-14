using FiveStack.Entities.Practice;
using SwiftlyS2.Shared.Commands;

namespace UtilityPractice;

// The panel's verbs for lineup discovery, over RCON like the other remote
// commands, and only on a server booked for renders: a discovery rains
// grenades on the map, and nobody plays on a render server.
public partial class UtilityPracticePlugin
{
    // Accepted and still being fetched: Start has not run yet, so the
    // discovery itself does not know the id, and a cancel or a second run in
    // that window has to be answered from here.
    private string? _discoveryFetching;
    [Command("utility_discovery_run", registerRaw: true, permission: "")]
    public void OnDiscoveryRun(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            return;
        }

        if (!_config.RenderMode)
        {
            Reply(context, "discovery runs on a render server only");
            return;
        }

        string id = DiscoveryId(context);

        if (!Guid.TryParse(id, out _))
        {
            Reply(context, "usage: utility_discovery_run id=<uuid>");
            return;
        }

        string? busyWith = _discoveryFetching ?? _discovery.JobId;

        if (busyWith != null)
        {
            Reply(context, $"busy with discovery {busyWith}");
            return;
        }

        _discoveryFetching = id;
        Reply(context, $"discovery {id} accepted");

        _ = Task.Run(async () =>
        {
            DiscoveryJob? job = await _api.DiscoveryJob(id);

            Core.Scheduler.NextTick(() =>
            {
                if (_discoveryFetching != id)
                {
                    _ = _api.DiscoveryDone(id, "cancelled");
                    return;
                }

                _discoveryFetching = null;

                if (job == null || job.id != id)
                {
                    _ = _api.DiscoveryDone(id, "the server could not fetch the discovery");
                    return;
                }

                // One after another: the panel only takes a report while the
                // discovery is open, so the last outcomes must land before done.
                Task posted = Task.CompletedTask;

                bool started = _discovery.Start(
                    job,
                    outcomes => posted = posted.ContinueWith(_ => _api.DiscoveryResults(id, outcomes)).Unwrap(),
                    error => posted = posted.ContinueWith(_ => _api.DiscoveryDone(id, error)).Unwrap()
                );

                if (!started)
                {
                    _ = _api.DiscoveryDone(id, "another discovery is already running on this server");
                }
            });
        });
    }

    [Command("utility_discovery_cancel", registerRaw: true, permission: "")]
    public void OnDiscoveryCancel(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            return;
        }

        string id = DiscoveryId(context);

        if (_discoveryFetching != null && _discoveryFetching == id)
        {
            _discoveryFetching = null;
            Reply(context, $"discovery {id} cancelled");
            return;
        }

        if (_discovery.JobId != id || !_discovery.Cancel())
        {
            Reply(context, "no such discovery running");
            return;
        }

        Reply(context, $"discovery {id} cancelled");
    }

    private static string DiscoveryId(ICommandContext context)
    {
        string first = context.Args.FirstOrDefault() ?? "";

        return (first.StartsWith("id=", StringComparison.Ordinal) ? first[3..] : first).Trim().Trim('"');
    }
}
