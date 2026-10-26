using System.Security.Cryptography;
using System.Text;
using FiveStack.Entities;

namespace FiveStack.Simulator;

public sealed class SimPlayer
{
    public required string SteamId { get; init; }
    public required string Name { get; init; }
    public required Guid LineupId { get; init; }
    public bool Placeholder { get; init; }
    public bool FlaggedCaptain { get; init; }

    // How well this player plays, fixed for the match.
    public double Skill { get; init; } = 1;

    public int Money { get; set; }
    public int Health { get; set; }
    public int Armor { get; set; }
    public Loadout Loadout { get; set; } = Loadout.Pistol(Side.T);
    public bool Alive => Health > 0;
}

public sealed class Roster
{
    private const ulong SteamIdBase = 76561197960265728UL;

    public required Guid Lineup1Id { get; init; }
    public required Guid Lineup2Id { get; init; }
    public required List<SimPlayer> Lineup1 { get; init; }
    public required List<SimPlayer> Lineup2 { get; init; }

    public IEnumerable<SimPlayer> All => Lineup1.Concat(Lineup2);

    public List<SimPlayer> Of(Guid lineupId)
    {
        return lineupId == Lineup1Id ? Lineup1 : Lineup2;
    }

    public bool HasPlaceholders => All.Any(player => player.Placeholder);

    public static Roster From(MatchData match, Random random)
    {
        return new Roster
        {
            Lineup1Id = match.lineup_1_id,
            Lineup2Id = match.lineup_2_id,
            Lineup1 = Players(match, match.lineup_1, match.lineup_1_id, random),
            Lineup2 = Players(match, match.lineup_2, match.lineup_2_id, random),
        };
    }

    private static List<SimPlayer> Players(
        MatchData match,
        MatchLineUp lineup,
        Guid lineupId,
        Random random
    )
    {
        var players = new List<SimPlayer>();

        for (int index = 0; index < lineup.lineup_players.Count; index++)
        {
            MatchMember member = lineup.lineup_players[index];
            bool placeholder = string.IsNullOrEmpty(member.steam_id);
            string name =
                !string.IsNullOrEmpty(member.name) ? member.name
                : !string.IsNullOrEmpty(member.placeholder_name) ? member.placeholder_name
                : $"Player {index + 1}";

            players.Add(
                new SimPlayer
                {
                    SteamId = placeholder
                        ? PlaceholderSteamId(match.id, lineupId, index)
                        : member.steam_id!,
                    Name = name,
                    LineupId = lineupId,
                    Placeholder = placeholder,
                    FlaggedCaptain = member.captain,
                    Skill = 0.75 + random.NextDouble() * 0.5,
                }
            );
        }

        return players;
    }

    // A placeholder has no Steam account; the one made up for it stays the same
    // for the whole match so the api can pin the placeholder to it.
    public static string PlaceholderSteamId(Guid matchId, Guid lineupId, int index)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{matchId}:{lineupId}:{index}"));
        uint accountId = (BitConverter.ToUInt32(hash, 0) & 0x7FFFFFFF) | 1;

        return (SteamIdBase + accountId).ToString();
    }
}
