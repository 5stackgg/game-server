using System.Text;
using System.Text.RegularExpressions;

namespace FiveStack.Simulator;

// What a round's backup file holds, in the shape of CS2's: the round, every
// player's cash, and here the score, which a restore reads back.
public static class SimulatedBackup
{
    private const ulong SteamIdBase = 76561197960265728;

    public static string Write(int round, int lineup1Score, int lineup2Score, Roster roster)
    {
        var file = new StringBuilder();
        file.Append("\"SaveFile\"\n{\n");
        file.Append($"\t\"round\"\t\t\"{round}\"\n");
        file.Append($"\t\"lineup_1_score\"\t\t\"{lineup1Score}\"\n");
        file.Append($"\t\"lineup_2_score\"\t\t\"{lineup2Score}\"\n");

        foreach (
            (string team, List<SimPlayer> lineup) in new[]
            {
                ("PlayersOnTeam1", roster.Lineup1),
                ("PlayersOnTeam2", roster.Lineup2),
            }
        )
        {
            file.Append($"\t\"{team}\"\n\t{{\n");
            foreach (SimPlayer player in lineup)
            {
                if (!ulong.TryParse(player.SteamId, out ulong steamId) || steamId <= SteamIdBase)
                {
                    continue;
                }

                ulong accountId = steamId - SteamIdBase;
                file.Append($"\t\t\"{accountId}\"\n\t\t{{\n");
                file.Append($"\t\t\t\"name\"\t\t\"{player.Name}\"\n");
                file.Append($"\t\t\t\"cash\"\t\t\"{player.Money}\"\n");
                file.Append("\t\t}\n");
            }
            file.Append("\t}\n");
        }

        file.Append("}\n");
        return file.ToString();
    }

    public static (int Lineup1, int Lineup2) Scores(string backup)
    {
        return (Value(backup, "lineup_1_score"), Value(backup, "lineup_2_score"));
    }

    private static int Value(string backup, string key)
    {
        Match match = Regex.Match(backup, $"\"{key}\"\\s+\"(\\d+)\"");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }
}
