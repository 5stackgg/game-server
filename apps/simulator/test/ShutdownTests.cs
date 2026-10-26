using System.Diagnostics;
using Xunit;

public class ShutdownTests
{
    // The panel stops a match server with `kill -SIGUSR1 1`, and the real
    // image's entrypoint answers it with a clean exit.
    [Theory]
    [InlineData("USR1")]
    [InlineData("TERM")]
    public async Task TheServerStopsCleanlyWhenSignalled(string signal)
    {
        var start = new ProcessStartInfo(
            "dotnet",
            Path.Combine(AppContext.BaseDirectory, "simulator.dll")
        )
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.Environment["SERVER_PORT"] = "0";
        start.Environment.Remove("SERVER_ID");

        using Process server = Process.Start(start)!;
        var output = new List<string>();
        var listening = new TaskCompletionSource();
        server.OutputDataReceived += (_, line) =>
        {
            if (line.Data == null)
            {
                return;
            }
            lock (output)
            {
                output.Add(line.Data);
            }
            if (line.Data.Contains("rcon listening"))
            {
                listening.TrySetResult();
            }
        };
        server.BeginOutputReadLine();

        await listening.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Process.Start("kill", $"-{signal} {server.Id}")!.WaitForExit();

        await server.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, server.ExitCode);
        lock (output)
        {
            Assert.Contains(output, line => line.Contains("stopped"));
        }
    }
}
