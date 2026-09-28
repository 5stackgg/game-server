using FiveStack.Entities;
using FiveStack.Enums;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace FiveStack.Utilities
{
    public static class MatchUtility
    {
        public static ISwiftlyCore Core { get; private set; } = null!;

        public static void Initialize(ISwiftlyCore core)
        {
            Core = core;
        }

        public static string GetSafeMatchPrefix(MatchData matchData)
        {
            return $"{matchData.id}_{matchData.current_match_map_id}".Replace("-", "");
        }

        public static bool HasPlaceholderMembers(MatchData matchData)
        {
            return matchData
                .lineup_1.lineup_players.Concat(matchData.lineup_2.lineup_players)
                .Any(member => member.steam_id == null);
        }

        public static MatchMember? GetMemberFromLineup(
            MatchData matchData,
            string steamId,
            string playerName
        )
        {
            List<MatchMember> players = matchData
                .lineup_1.lineup_players.Concat(matchData.lineup_2.lineup_players)
                .ToList();

            return players.Find(member =>
            {
                if (member.steam_id == null)
                {
                    return member.placeholder_name.StartsWith(playerName);
                }

                return member.steam_id == steamId;
            });
        }

        public static string? GetTeamChatLineupId(
            MatchData matchData,
            string steamId,
            string playerName
        )
        {
            Guid lineupId =
                GetMemberFromLineup(matchData, steamId, playerName)?.match_lineup_id ?? Guid.Empty;

            if (lineupId == Guid.Empty)
            {
                if (
                    !string.IsNullOrEmpty(matchData.lineup_1.coach_steam_id)
                    && matchData.lineup_1.coach_steam_id == steamId
                )
                {
                    lineupId = matchData.lineup_1.id;
                }
                else if (
                    !string.IsNullOrEmpty(matchData.lineup_2.coach_steam_id)
                    && matchData.lineup_2.coach_steam_id == steamId
                )
                {
                    lineupId = matchData.lineup_2.id;
                }
            }

            if (lineupId == Guid.Empty)
            {
                return null;
            }

            return lineupId.ToString();
        }

        // A client presenting the raw match password is a streamer, unless the
        // lineup still has placeholder seats: then it may be the player
        // meant to fill one.
        public static ConnectRules GetConnectRules(
            MatchData matchData,
            ulong steamId,
            string playerName
        )
        {
            List<MatchMember> players = matchData
                .lineup_1.lineup_players.Concat(matchData.lineup_2.lineup_players)
                .ToList();

            return new ConnectRules
            {
                match_id = matchData.id,
                password = matchData.password,
                is_member = GetMemberFromLineup(matchData, steamId.ToString(), playerName) != null,
                password_role = HasPlaceholderMembers(matchData) ? null : "streamer",
                roster = players
                    .Select(member => member.steam_id ?? $"placeholder '{member.placeholder_name}'")
                    .ToList(),
            };
        }

        public static Guid? GetPlayerLineup(MatchData matchData, IPlayer player)
        {
            MatchMember? member = GetMemberFromLineup(
                matchData,
                player.SteamID.ToString(),
                player.Name
            );

            if (member == null)
            {
                return null;
            }

            return member.match_lineup_id;
        }

        public static string? GetPlayerLineupTag(MatchData matchData, IPlayer player)
        {
            Guid? lineup_id = GetPlayerLineup(matchData, player);

            string tag =
                matchData.lineup_1_id == lineup_id
                    ? matchData.lineup_1.tag
                    : matchData.lineup_2.tag;

            if (string.IsNullOrEmpty(tag))
            {
                return null;
            }

            return tag.Trim();
        }

        public static eMapStatus MapStatusStringToEnum(string state)
        {
            switch (state)
            {
                case "Scheduled":
                    return eMapStatus.Scheduled;
                case "Finished":
                    return eMapStatus.Finished;
                case "Knife":
                    return eMapStatus.Knife;
                case "Live":
                    return eMapStatus.Live;
                case "Overtime":
                    return eMapStatus.Overtime;
                case "Paused":
                    return eMapStatus.Paused;
                case "Warmup":
                    return eMapStatus.Warmup;
                case "WaitingForTV":
                    return eMapStatus.WaitingForTV;
                case "UploadingDemo":
                    return eMapStatus.UploadingDemo;
                case "Surrendered":
                    return eMapStatus.Surrendered;
                case "Unknown":
                    return eMapStatus.Unknown;
                default:
                    throw new ArgumentException($"Unsupported status string: {state}");
            }
        }

        private static CCSGameRules? _cachedRules;
        private static List<CCSTeam>? _cachedTeams;

        public static void InvalidateCache()
        {
            _cachedRules = null;
            _cachedTeams = null;
        }

        public static CCSGameRules? Rules()
        {
            if (_cachedRules != null)
            {
                return _cachedRules;
            }

            _cachedRules = Core
                .EntitySystem.GetAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .FirstOrDefault()
                ?.GameRules;

            return _cachedRules;
        }

        public static List<IPlayer> Players()
        {
            List<IPlayer> validPlayers = new List<IPlayer>();

            foreach (var player in Core.PlayerManager.GetAllPlayers())
            {
                if (
                    player == null
                    || !player.IsValid
                    || player.IsFakeClient
                    || player.Controller == null
                    || player.Name == "SourceTV"
                )
                {
                    continue;
                }

                validPlayers.Add(player);
            }

            return validPlayers;
        }

        public static int PlayerCount()
        {
            int count = 0;

            foreach (var player in Core.PlayerManager.GetAllPlayers())
            {
                if (
                    player == null
                    || !player.IsValid
                    || player.IsFakeClient
                    || player.Controller == null
                    || player.Name == "SourceTV"
                )
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        public static HashSet<string> RosterSteamIds(MatchData matchData)
        {
            return matchData
                .lineup_1.lineup_players.Concat(matchData.lineup_2.lineup_players)
                .Select(member => member.steam_id)
                .Where(steamId => !string.IsNullOrEmpty(steamId))
                .Select(steamId => steamId!)
                .ToHashSet();
        }

        // How many of the two lineups are actually in the server right now.
        // Players() also returns casters and admins, who must never count
        // towards a match being whole again.
        public static int ConnectedRosterCount(MatchData matchData)
        {
            HashSet<string> roster = RosterSteamIds(matchData);

            return Players().Count(player => roster.Contains(player.SteamID.ToString()));
        }

        public static IEnumerable<CCSTeam> Teams()
        {
            if (_cachedTeams != null)
            {
                return _cachedTeams;
            }

            List<CCSTeam> teams = Core
                .EntitySystem.GetAllEntitiesByDesignerName<CCSTeam>("cs_team_manager")
                .ToList();

            if (teams.Count > 0)
            {
                _cachedTeams = teams;
            }

            return teams;
        }
    }
}
