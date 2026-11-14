using FiveStack.Entities;
using FiveStack.Enums;

namespace FiveStack.Utilities;

public static class ReconnectPauseUtility
{
    // Coaches, casters and admins are in the server too, so only the lineups'
    // own players say whether anyone is missing. Each lineup is held to its own
    // size: a lineup short by design (a 5v4 started by its captains or an admin)
    // is not missing anyone. A pickup seat is only waited on once linking Discord
    // has bound it to its player's steam id: a seat nobody holds cannot reconnect.
    public static bool ShouldPause(
        MatchData matchData,
        IEnumerable<(string SteamId, string Name)> connected,
        int expectedPlayers
    )
    {
        (int present, int required) = RosterCount(matchData, connected, expectedPlayers);

        return present < required;
    }

    // Only the pause the roster made is ended by finding everyone back, and only
    // for one of the lineups' own players: any other waits for whoever called
    // it, and so does one the teams, a camera or a timeout still hold. A round
    // held for a resume with no pause on the map, as at halftime, is one the
    // roster did not make.
    public static eRosterCheck Check(
        ePauseReason? pause,
        bool hasPauseToResume,
        bool shortHanded,
        bool held,
        bool callerInLineup
    )
    {
        if (pause == null && !hasPauseToResume)
        {
            return eRosterCheck.NotPaused;
        }

        if (shortHanded)
        {
            return eRosterCheck.Missing;
        }

        return pause == ePauseReason.Roster && !held && callerInLineup
            ? eRosterCheck.Resume
            : eRosterCheck.Whole;
    }

    // Each lineup's count is capped at what it needs, so a lineup with a spare
    // player in the server never makes up for one that is short.
    public static (int Present, int Required) RosterCount(
        MatchData matchData,
        IEnumerable<(string SteamId, string Name)> connected,
        int expectedPlayers
    )
    {
        return Count(matchData, connected, expectedPlayers, BoundSeats);
    }

    // Every seat, bound or not: readying up waits on a seat until its player
    // links Discord, or an admin forces the start.
    public static (int Present, int Required) SeatCount(
        MatchData matchData,
        IEnumerable<(string SteamId, string Name)> connected,
        int expectedPlayers
    )
    {
        return Count(matchData, connected, expectedPlayers, Seats);
    }

    private static (int Present, int Required) Count(
        MatchData matchData,
        IEnumerable<(string SteamId, string Name)> connected,
        int expectedPlayers,
        Func<MatchLineUp, int, int> seats
    )
    {
        int required1 = seats(matchData.lineup_1, expectedPlayers);
        int required2 = seats(matchData.lineup_2, expectedPlayers);
        int present1 = 0;
        int present2 = 0;

        foreach ((string steamId, string name) in connected)
        {
            int? lineup = LineupMembership.LineupNumber(matchData, steamId, name);

            if (lineup == 1)
            {
                present1++;
            }
            else if (lineup == 2)
            {
                present2++;
            }
        }

        return (
            Math.Min(present1, required1) + Math.Min(present2, required2),
            required1 + required2
        );
    }

    public static int Seats(MatchLineUp lineup, int expectedPlayers)
    {
        return Math.Min(expectedPlayers / 2, lineup.lineup_players.Count);
    }

    public static int BoundSeats(MatchLineUp lineup, int expectedPlayers)
    {
        return Math.Min(
            expectedPlayers / 2,
            lineup.lineup_players.Count(member => member.steam_id != null)
        );
    }

    public static List<Guid> ShortLineups(
        MatchData matchData,
        IEnumerable<(string SteamId, string Name)> connected,
        int expectedPlayers
    )
    {
        List<(string SteamId, string Name)> players = connected.ToList();

        return new[] { matchData.lineup_1_id, matchData.lineup_2_id }
            .Distinct()
            .Where(lineup => !IsLineupWhole(matchData, players, expectedPlayers, lineup))
            .ToList();
    }

    // A lineup with seats to fill and nobody in them leaves the other side
    // playing rounds out against no one.
    public static bool IsALineupEmpty(
        MatchData matchData,
        IEnumerable<(string SteamId, string Name)> connected,
        int expectedPlayers
    )
    {
        int present1 = 0;
        int present2 = 0;

        foreach ((string steamId, string name) in connected)
        {
            int? lineup = LineupMembership.LineupNumber(matchData, steamId, name);

            if (lineup == 1)
            {
                present1++;
            }
            else if (lineup == 2)
            {
                present2++;
            }
        }

        return (present1 == 0 && BoundSeats(matchData.lineup_1, expectedPlayers) > 0)
            || (present2 == 0 && BoundSeats(matchData.lineup_2, expectedPlayers) > 0);
    }

    // Whole once as many of its own players are in as it needs, so a lineup short
    // by design is whole with everyone it has.
    public static bool IsLineupWhole(
        MatchData matchData,
        IEnumerable<(string SteamId, string Name)> connected,
        int expectedPlayers,
        Guid lineupId
    )
    {
        int lineup =
            lineupId == matchData.lineup_1_id ? 1
            : lineupId == matchData.lineup_2_id ? 2
            : 0;

        if (lineup == 0)
        {
            return false;
        }

        int required = BoundSeats(
            lineup == 1 ? matchData.lineup_1 : matchData.lineup_2,
            expectedPlayers
        );

        return connected.Count(player =>
                LineupMembership.LineupNumber(matchData, player.SteamId, player.Name) == lineup
            ) >= required;
    }

    // Only someone who left while leaving was not timed. A player who never
    // connected was the match's deadline to settle, and a match started without
    // them was started that way on purpose; with a bench, they also look the
    // same as a substitute nobody needed.
    public static List<ulong> AbsentToTime(
        MatchData matchData,
        IEnumerable<(string SteamId, string Name)> connected,
        int expectedPlayers,
        IReadOnlySet<ulong> leftUntimed,
        IReadOnlySet<ulong> timed
    )
    {
        List<(string SteamId, string Name)> players = connected.ToList();
        string[] coaches = [matchData.lineup_1.coach_steam_id, matchData.lineup_2.coach_steam_id];
        List<ulong> absent = [];

        for (int number = 1; number <= 2; number++)
        {
            MatchLineUp lineup = number == 1 ? matchData.lineup_1 : matchData.lineup_2;
            int emptySeats =
                BoundSeats(lineup, expectedPlayers)
                - players.Count(player =>
                    LineupMembership.LineupNumber(matchData, player.SteamId, player.Name) == number
                );

            if (emptySeats <= 0)
            {
                continue;
            }

            List<ulong> missing = lineup
                .lineup_players.Select(member => member.steam_id)
                .OfType<string>()
                .Where(steamId =>
                    !coaches.Contains(steamId) && players.All(player => player.SteamId != steamId)
                )
                .Select(steamId => ulong.TryParse(steamId, out ulong id) ? id : 0)
                .Where(steamId => steamId != 0)
                .ToList();

            absent.AddRange(missing.Where(leftUntimed.Contains));
        }

        return absent.Where(steamId => !timed.Contains(steamId)).ToList();
    }
}
