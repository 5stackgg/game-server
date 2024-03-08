namespace FiveStack;

public static class DotEnv
{
    public static void Load(string filePath)
    {
        Console.WriteLine($"LOAD FILE PATH {filePath}");
        if (!File.Exists(filePath))
        {
            Console.WriteLine("Unable to read .env file");
            return;
        }

        foreach (var line in File.ReadAllLines(filePath))
        {
            var parts = line.Split('=', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
            {
                continue;
            }

            Console.WriteLine($"VARIABLE {parts[0]}:{parts[1]}");

            Environment.SetEnvironmentVariable(parts[0], parts[1]);
        }
    }
}
