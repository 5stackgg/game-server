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

            return $"[{tag.Trim()}]";
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
