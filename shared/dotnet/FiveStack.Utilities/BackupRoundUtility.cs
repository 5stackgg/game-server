using FiveStack.Entities;

namespace FiveStack.Utilities
{
    public static class BackupRoundUtility
    {
        // A backup only counts if CS2 can seat a team from it. A round that
        // ended on a near-empty server still writes a well-formed file, just
        // with no players in it -- restoring that leaves everyone unassigned.
        public static int MinPlayersPerTeam(int expectedPlayers)
        {
            return Math.Max(1, (expectedPlayers / 2 + 1) / 2);
        }

        // Null when the file is restorable, otherwise why it is not.
        public static string? Validate(string? backupFile, int expectedRound, int minPlayersPerTeam)
        {
            if (string.IsNullOrWhiteSpace(backupFile))
            {
                return "empty";
            }

            List<string> tokens = Tokenize(backupFile);

            if (tokens.Count < 2 || tokens[0] != "SaveFile" || tokens[1] != "{")
            {
                return "not a SaveFile";
            }

            int? round = null;
            Dictionary<string, int> teamPlayers = new Dictionary<string, int>();

            int depth = 0;
            for (int i = 1; i < tokens.Count; i++)
            {
                string token = tokens[i];

                if (token == "{")
                {
                    depth++;
                    continue;
                }

                if (token == "}")
                {
                    depth--;
                    continue;
                }

                if (depth != 1 || i + 1 >= tokens.Count)
                {
                    continue;
                }

                if (token == "round" && tokens[i + 1] != "{")
                {
                    if (int.TryParse(tokens[i + 1], out int parsed))
                    {
                        round = parsed;
                    }
                    i++;
                    continue;
                }

                if (token.StartsWith("PlayersOnTeam") && tokens[i + 1] == "{")
                {
                    teamPlayers[token] = CountSections(tokens, i + 1);
                }
            }

            if (round == null)
            {
                return "missing round";
            }

            if (round != expectedRound)
            {
                return $"round {round} does not match expected round {expectedRound}";
            }

            foreach (string team in new[] { "PlayersOnTeam1", "PlayersOnTeam2" })
            {
                int players = teamPlayers.GetValueOrDefault(team, 0);
                if (players < minPlayersPerTeam)
                {
                    return $"{team} has {players} player(s), need at least {minPlayersPerTeam}";
                }
            }

            return null;
        }

        public static bool IsDeleted(BackupRound backupRound)
        {
            return !string.IsNullOrEmpty(backupRound.deleted_at);
        }

        // Last entry wins: a replayed round is newer than the one it replaced.
        public static BackupRound? FindRestorable(
            IEnumerable<BackupRound> rounds,
            int round,
            int minPlayersPerTeam
        )
        {
            return rounds.LastOrDefault(backupRound =>
                backupRound.round == round
                && !IsDeleted(backupRound)
                && Validate(backupRound.backup_file, round, minPlayersPerTeam) == null
            );
        }

        public static int HighestRound(IEnumerable<BackupRound> rounds)
        {
            return rounds
                .Where(backupRound => !IsDeleted(backupRound))
                .Select(backupRound => backupRound.round)
                .DefaultIfEmpty(0)
                .Max();
        }

        public static int HighestRestorableRound(
            IEnumerable<BackupRound> rounds,
            int minPlayersPerTeam
        )
        {
            return rounds
                .Where(backupRound =>
                    !IsDeleted(backupRound)
                    && Validate(backupRound.backup_file, backupRound.round, minPlayersPerTeam)
                        == null
                )
                .Select(backupRound => backupRound.round)
                .DefaultIfEmpty(0)
                .Max();
        }

        public static BackupRound[] Upsert(BackupRound[] rounds, BackupRound backupRound)
        {
            return rounds
                .Where(existing => existing.round != backupRound.round)
                .Append(backupRound)
                .ToArray();
        }

        // Rounds past a restore point are void the moment the restore runs,
        // whether or not the backend has been heard from yet.
        public static BackupRound[] DropAbove(BackupRound[] rounds, int round)
        {
            return rounds.Where(existing => existing.round <= round).ToArray();
        }

        private static int CountSections(List<string> tokens, int openIndex)
        {
            int count = 0;
            int depth = 0;

            for (int i = openIndex; i < tokens.Count; i++)
            {
                if (tokens[i] == "{")
                {
                    depth++;
                    if (depth == 2)
                    {
                        count++;
                    }
                }
                else if (tokens[i] == "}")
                {
                    depth--;
                    if (depth == 0)
                    {
                        break;
                    }
                }
            }

            return count;
        }

        // Braces inside a quoted value (a player name) are text, not structure.
        private static List<string> Tokenize(string text)
        {
            List<string> tokens = new List<string>();

            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];

                if (c == '{' || c == '}')
                {
                    tokens.Add(c.ToString());
                    i++;
                    continue;
                }

                if (c != '"')
                {
                    i++;
                    continue;
                }

                System.Text.StringBuilder value = new System.Text.StringBuilder();
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i++;
                    }
                    value.Append(text[i]);
                    i++;
                }
                i++;

                // A quoted brace must not read as structure.
                string token = value.ToString();
                tokens.Add(token == "{" || token == "}" ? $"\"{token}\"" : token);
            }

            return tokens;
        }
    }
}
