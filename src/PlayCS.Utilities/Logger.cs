namespace PlayCs;

public partial class PlayCsPlugin
{
	public enum LogLevel
	{
		Debug = -1,
		Info,
		Warning,
		Error
	}

	public void Log(string message, LogLevel level = LogLevel.Info)
	{
		string logLevelString = LogLevelToString(level);
		string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevelString}] > {message}";

		Console.ForegroundColor = GetConsoleColor(level);
		Console.WriteLine(logMessage);
		Console.ResetColor();
	}

	private string LogLevelToString(LogLevel level)
	{
		switch (level)
		{
			case LogLevel.Debug:
				return "DEBUG";
			case LogLevel.Info:
				return "INFO";
			case LogLevel.Warning:
				return "WARNING";
			case LogLevel.Error:
				return "ERROR";
			default:
				return "UNKNOWN";
		}
	}

	private static ConsoleColor GetConsoleColor(LogLevel level)
	{
		switch (level)
		{
			case LogLevel.Debug:
				return ConsoleColor.Gray;
			case LogLevel.Info:
				return ConsoleColor.White;
			case LogLevel.Warning:
				return ConsoleColor.Yellow;
			case LogLevel.Error:
				return ConsoleColor.Red;
			default:
				return ConsoleColor.Gray;
		}
	}
}
