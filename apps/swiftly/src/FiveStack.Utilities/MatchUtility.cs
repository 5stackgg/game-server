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

        public static eAllChatRoute AllChatRoute(
            MatchData? matchData,
            string steamId,
            string playerName,
            bool spectator
        )
        {
            if (matchData != null && IsGagged(matchData, steamId, playerName))
            {
                return eAllChatRoute.Block;
            }

            if (spectator)
            {
                return eAllChatRoute.Spectator;
            }

            if (matchData == null)
            {
                return eAllChatRoute.NoMatch;
            }

            return eAllChatRoute.Publish;
        }

        public static string? GetTeamChatRelayLineupId(
            MatchData matchData,
            string steamId,
            string playerName
        )
        {
            if (!matchData.relay_team_chat)
            {
                return null;
            }

            if (IsGagged(matchData, steamId, playerName))
            {
                return null;
            }

            return GetTeamChatLineupId(matchData, steamId, playerName);
        }

        public static bool IsGagged(MatchData matchData, string steamId, string playerName)
        {
            return GetMemberFromLineup(matchData, steamId, playerName)?.is_gagged == true
                || matchData
                    .lineup_1.lineup_players.Concat(matchData.lineup_2.lineup_players)
                    .Any(member => member.is_gagged && member.steam_id == steamId);
        }

        // A lineup_1 placeholder whose name prefixes a lineup_2 player would
        // otherwise claim them, so exact steam ids are matched before names.
        public static string? GetTeamChatLineupId(
            MatchData matchData,
            string steamId,
            string playerName
        )
        {
            List<MatchMember> players = matchData
                .lineup_1.lineup_players.Concat(matchData.lineup_2.lineup_players)
                .ToList();

            Guid lineupId =
                players
                    .Find(member =>
                        !string.IsNullOrEmpty(member.steam_id) && member.steam_id == steamId
                    )
                    ?.match_lineup_id
                ?? GetCoachLineupId(matchData, steamId)
                ?? players
                    .Find(member =>
                        member.steam_id == null && member.placeholder_name.StartsWith(playerName)
                    )
                    ?.match_lineup_id
                ?? Guid.Empty;

            if (lineupId == Guid.Empty)
            {
                return null;
            }

            return lineupId.ToString();
        }

        private static Guid? GetCoachLineupId(MatchData matchData, string steamId)
        {
            if (!matchData.options.coaches || string.IsNullOrEmpty(steamId))
            {
                return null;
            }

            if (matchData.lineup_1.coach_steam_id == steamId)
            {
                return matchData.lineup_1.id;
            }

            if (matchData.lineup_2.coach_steam_id == steamId)
            {
                return matchData.lineup_2.id;
            }

            return null;
        }

        public static (string Event, Dictionary<string, object> Data) ChatEvent(
            string steamId,
            string message
        )
        {
            return (
                "chat",
                new Dictionary<string, object> { { "player", steamId }, { "message", message } }
            );
        }

        // Team lines go out under their own event so an api that predates
        // them drops them as unknown, instead of treating them as all chat and
        // posting them where the other team reads.
        public static (string Event, Dictionary<string, object> Data)? TeamChatEvent(
            MatchData matchData,
            string steamId,
            string playerName,
            string message
        )
        {
            string? lineupId = GetTeamChatRelayLineupId(matchData, steamId, playerName);

            if (lineupId == null)
            {
                return null;
            }

            return (
                "teamChat",
                new Dictionary<string, object>
                {
                    { "player", steamId },
                    { "message", message },
                    { "lineupId", lineupId },
                }
            );
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
