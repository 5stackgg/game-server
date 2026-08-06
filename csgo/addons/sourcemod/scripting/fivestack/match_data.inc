/**
 * 5Stack CS:GO Plugin - Match Data Structures
 * Mirrors FiveStack.Entities from CS2 plugin.
 * Uses enum structs (SourcePawn 1.10+).
 */

#if defined _fivestack_match_data_included
    #endinput
#endif
#define _fivestack_match_data_included

#define MAX_PLAYERS_PER_TEAM  5
#define MAX_LINEUP_PLAYERS    (MAX_PLAYERS_PER_TEAM + 5) // players + substitutes
#define MAX_MATCH_MAPS        5
#define GUID_LENGTH           37  // 36 chars + null
#define MAX_NAME_LENGTH       128
#define MAX_STEAMID_LENGTH    32
#define MAX_PASSWORD_LENGTH   64
#define MAX_MAP_NAME_LENGTH   64
#define MAX_SIDE_LENGTH       16
#define MAX_TAG_LENGTH        32
#define MAX_ROLE_LENGTH       32
#define MAX_STATUS_LENGTH     32
#define MAX_TYPE_LENGTH       32
#define MAX_WORKSHOP_ID_LEN   32

enum struct MatchMember
{
    char name[MAX_NAME_LENGTH];
    char role[MAX_ROLE_LENGTH];
    char placeholder_name[MAX_NAME_LENGTH];
    char steam_id[MAX_STEAMID_LENGTH];
    bool captain;
    char match_lineup_id[GUID_LENGTH];
    bool is_banned;
    bool is_gagged;
    bool is_muted;
}

enum struct MapDef
{
    char name[MAX_MAP_NAME_LENGTH];
    char workshop_map_id[MAX_WORKSHOP_ID_LEN];
}

enum struct MatchMap
{
    char id[GUID_LENGTH];
    MapDef map;
    int order;
    char status[MAX_STATUS_LENGTH];
    char lineup_1_side[MAX_SIDE_LENGTH];
    char lineup_2_side[MAX_SIDE_LENGTH];
    int lineup_1_timeouts_available;
    int lineup_2_timeouts_available;
}

enum struct MatchLineUp
{
    char id[GUID_LENGTH];
    char name[MAX_NAME_LENGTH];
    char tag[MAX_TAG_LENGTH];
    char coach_steam_id[MAX_STEAMID_LENGTH];
    MatchMember players[MAX_LINEUP_PLAYERS];
    int player_count;
}

enum struct MatchOptions
{
    int mr;
    char type[MAX_TYPE_LENGTH];         // "Competitive", "Wingman", "Duel"
    bool overtime;
    int best_of;
    int tv_delay;
    bool coaches;
    int number_of_substitutes;
    bool knife_round;
    bool default_models;
    char ready_setting[MAX_TYPE_LENGTH];
    char timeout_setting[MAX_TYPE_LENGTH];
    char tech_timeout_setting[MAX_TYPE_LENGTH];
}

enum struct MatchData
{
    char id[GUID_LENGTH];
    bool is_lan;
    char password[MAX_PASSWORD_LENGTH];
    char current_match_map_id[GUID_LENGTH];

    MatchMap match_maps[MAX_MATCH_MAPS];
    int match_map_count;

    MatchOptions options;

    char lineup_1_id[GUID_LENGTH];
    char lineup_2_id[GUID_LENGTH];
    MatchLineUp lineup_1;
    MatchLineUp lineup_2;
}
