using System.Runtime.InteropServices;
using FiveStack.Simulator;

SimulatorSettings settings = SimulatorSettings.FromEnvironment(
    Environment.GetEnvironmentVariables()
);

using var shutdown = new CancellationTokenSource();
using var sigterm = PosixSignalRegistration.Create(
    PosixSignal.SIGTERM,
    context =>
    {
        context.Cancel = true;
        shutdown.Cancel();
    }
);
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    shutdown.Cancel();
};

Log.Info(
    $"5Stack simulated game server: server {settings.ServerId ?? "unassigned"}, map {settings.StartMap}, x{settings.Speed} speed"
);

var api = new ApiClient(settings);
SimulatedServer? server = null;
var socket = new EventSocket(settings, () => server!.RefreshMatch());
server = new SimulatedServer(
    settings,
    api,
    socket,
    new ScaledPacer(settings.Speed),
    settings.Seed is int seed ? new Random(seed) : new Random(),
    shutdown.Token
);
var rcon = new RconServer(settings.Port, settings.RconPassword, server.Execute);

async Task Ping()
{
    if (settings.ServerId == null || settings.ApiPassword == null)
    {
        return;
    }

    try
    {
        await api.Ping(server.LoadedMap, server.Players, shutdown.Token);
    }
    catch (Exception error) when (!shutdown.IsCancellationRequested)
    {
        Log.Warn($"unable to ping: {error.Message}");
    }
}

server.MapLoaded = () => _ = Ping();

async Task Heartbeat()
{
    while (!shutdown.IsCancellationRequested)
    {
        await Ping();

        // players wait for the api to call the match Live, which it follows
        // with get_match; asking again covers an RCON that never arrives
        if (server.Run?.WaitingForLive == true)
        {
            await server.RefreshMatch();
        }

        await Task.Delay(TimeSpan.FromSeconds(15), shutdown.Token);
    }
}

try
{
    await Task.WhenAll(rcon.Run(shutdown.Token), socket.Run(shutdown.Token), Heartbeat());
}
catch (OperationCanceledException) { }

Log.Info("stopped");
