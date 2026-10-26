namespace FiveStack.Simulator;

public interface IPacer
{
    Task Wait(double gameSeconds, CancellationToken token);
}

public sealed class ScaledPacer(double speed) : IPacer
{
    public Task Wait(double gameSeconds, CancellationToken token)
    {
        if (gameSeconds <= 0)
        {
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        return Task.Delay(TimeSpan.FromSeconds(gameSeconds / speed), token);
    }
}

public static class Log
{
    public static void Info(string message)
    {
        Console.WriteLine($"{DateTimeOffset.UtcNow:O} {message}");
    }

    public static void Warn(string message)
    {
        Console.WriteLine($"{DateTimeOffset.UtcNow:O} WARN {message}");
    }
}
