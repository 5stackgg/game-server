using FiveStack.Entities.Practice;
using SwiftlyS2.Shared.Commands;

namespace UtilityPractice;

// The panel's verbs for lineup discovery, over RCON like the other remote
// commands, and only on a server booked for renders: a discovery rains
// grenades on the map, and nobody plays on a render server.
public partial class UtilityPracticePlugin
{
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

        if (_discovery.IsBusy)
        {
            Reply(context, $"busy with discovery {_discovery.JobId}");
            return;
        }

        Reply(context, $"discovery {id} accepted");

        _ = Task.Run(async () =>
        {
            DiscoveryJob? job = await _api.DiscoveryJob(id);

            if (job == null || job.id != id)
            {
                await _api.DiscoveryDone(id, "the server could not fetch the discovery");
                return;
            }

            Core.Scheduler.NextTick(() =>
            {
                bool started = _discovery.Start(
                    job,
                    outcomes => _ = _api.DiscoveryResults(id, outcomes),
                    error => _ = _api.DiscoveryDone(id, error)
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
