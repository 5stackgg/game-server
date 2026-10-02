/**
 * 5Stack CS:GO Plugin - JSON Helpers
 * Utilities for building/parsing JSON using sm-ripext.
 */

#if defined _fivestack_json_helpers_included
    #endinput
#endif
#define _fivestack_json_helpers_included

#include <ripext>

// Generate a pseudo-UUID v4 (not cryptographically secure, but unique enough for message IDs)
stock void GenerateUUID(char[] buffer, int maxlen)
{
    int r1 = GetRandomInt(0, 0x7FFFFFFF);
    int r2 = GetRandomInt(0, 0x7FFFFFFF);
    int r3 = GetRandomInt(0, 0x7FFFFFFF);
    int r4 = GetRandomInt(0, 0x7FFFFFFF);

    FormatEx(buffer, maxlen, "%08x-%04x-4%03x-%04x-%04x%08x",
        r1,
        (r2 >> 16) & 0xFFFF,
        r2 & 0x0FFF,
        (r3 >> 16 & 0x3FFF) | 0x8000,
        r3 & 0xFFFF,
        r4);
}

// Safe JSON string getter that handles missing keys
stock bool JSON_GetStringOrDefault(JSONObject json, const char[] key, char[] buffer, int maxlen, const char[] defaultVal = "")
{
    if (json.HasKey(key) && !json.IsNull(key))
    {
        json.GetString(key, buffer, maxlen);
        return true;
    }
    strcopy(buffer, maxlen, defaultVal);
    return false;
}

// Safe JSON int getter
stock int JSON_GetIntOrDefault(JSONObject json, const char[] key, int defaultVal = 0)
{
    if (json.HasKey(key) && !json.IsNull(key))
    {
        return json.GetInt(key);
    }
    return defaultVal;
}

// Safe JSON bool getter
stock bool JSON_GetBoolOrDefault(JSONObject json, const char[] key, bool defaultVal = false)
{
    if (json.HasKey(key) && !json.IsNull(key))
    {
        return json.GetBool(key);
    }
    return defaultVal;
}

// Parse a MatchMember from JSON
stock void JSON_ParseMatchMember(JSONObject json, MatchMember member)
{
    JSON_GetStringOrDefault(json, "name", member.name, sizeof(MatchMember::name));
    JSON_GetStringOrDefault(json, "role", member.role, sizeof(MatchMember::role));
    JSON_GetStringOrDefault(json, "placeholder_name", member.placeholder_name, sizeof(MatchMember::placeholder_name));
    JSON_GetStringOrDefault(json, "steam_id", member.steam_id, sizeof(MatchMember::steam_id));
    JSON_GetStringOrDefault(json, "match_lineup_id", member.match_lineup_id, sizeof(MatchMember::match_lineup_id));
    member.captain = JSON_GetBoolOrDefault(json, "captain");
    member.is_banned = JSON_GetBoolOrDefault(json, "is_banned");
    member.is_gagged = JSON_GetBoolOrDefault(json, "is_gagged");
    member.is_muted = JSON_GetBoolOrDefault(json, "is_muted");
}

// Parse a MatchLineUp from JSON
stock void JSON_ParseMatchLineUp(JSONObject json, MatchLineUp lineup)
{
    JSON_GetStringOrDefault(json, "id", lineup.id, sizeof(MatchLineUp::id));
    JSON_GetStringOrDefault(json, "name", lineup.name, sizeof(MatchLineUp::name));
    JSON_GetStringOrDefault(json, "tag", lineup.tag, sizeof(MatchLineUp::tag));
    JSON_GetStringOrDefault(json, "coach_steam_id", lineup.coach_steam_id, sizeof(MatchLineUp::coach_steam_id));

    lineup.player_count = 0;
    if (json.HasKey("lineup_players") && !json.IsNull("lineup_players"))
    {
        JSONArray players = view_as<JSONArray>(json.Get("lineup_players"));
        int count = players.Length;
        if (count > MAX_LINEUP_PLAYERS) count = MAX_LINEUP_PLAYERS;

        for (int i = 0; i < count; i++)
        {
            JSONObject playerJson = view_as<JSONObject>(players.Get(i));
            JSON_ParseMatchMember(playerJson, lineup.players[lineup.player_count]);
            lineup.player_count++;
            delete playerJson;
        }
        delete players;
    }
}

// Parse MatchOptions from JSON
stock void JSON_ParseMatchOptions(JSONObject json, MatchOptions options)
{
    options.mr = JSON_GetIntOrDefault(json, "mr", 12);
    JSON_GetStringOrDefault(json, "type", options.type, sizeof(MatchOptions::type), "Competitive");
    options.overtime = JSON_GetBoolOrDefault(json, "overtime", true);
    options.best_of = JSON_GetIntOrDefault(json, "best_of", 1);
    options.tv_delay = JSON_GetIntOrDefault(json, "tv_delay", 115);
    options.coaches = JSON_GetBoolOrDefault(json, "coaches", true);
    options.number_of_substitutes = JSON_GetIntOrDefault(json, "number_of_substitutes", 0);
    options.knife_round = JSON_GetBoolOrDefault(json, "knife_round", true);
    options.default_models = JSON_GetBoolOrDefault(json, "default_models", false);
    JSON_GetStringOrDefault(json, "ready_setting", options.ready_setting, sizeof(MatchOptions::ready_setting), "Players");
    JSON_GetStringOrDefault(json, "timeout_setting", options.timeout_setting, sizeof(MatchOptions::timeout_setting), "CoachAndPlayers");
    JSON_GetStringOrDefault(json, "tech_timeout_setting", options.tech_timeout_setting, sizeof(MatchOptions::tech_timeout_setting), "CoachAndPlayers");
}

// Parse a MatchMap from JSON
stock void JSON_ParseMatchMap(JSONObject json, MatchMap matchMap)
{
    JSON_GetStringOrDefault(json, "id", matchMap.id, sizeof(MatchMap::id));
    matchMap.order = JSON_GetIntOrDefault(json, "order", 1);
    JSON_GetStringOrDefault(json, "status", matchMap.status, sizeof(MatchMap::status), "Scheduled");
    JSON_GetStringOrDefault(json, "lineup_1_side", matchMap.lineup_1_side, sizeof(MatchMap::lineup_1_side), "CT");
    JSON_GetStringOrDefault(json, "lineup_2_side", matchMap.lineup_2_side, sizeof(MatchMap::lineup_2_side), "TERRORIST");
    matchMap.lineup_1_timeouts_available = JSON_GetIntOrDefault(json, "lineup_1_timeouts_available", 3);
    matchMap.lineup_2_timeouts_available = JSON_GetIntOrDefault(json, "lineup_2_timeouts_available", 3);

    if (json.HasKey("map") && !json.IsNull("map"))
    {
        JSONObject mapJson = view_as<JSONObject>(json.Get("map"));
        JSON_GetStringOrDefault(mapJson, "name", matchMap.map.name, sizeof(MapDef::name));
        JSON_GetStringOrDefault(mapJson, "workshop_map_id", matchMap.map.workshop_map_id, sizeof(MapDef::workshop_map_id));
        delete mapJson;
    }
}

// Parse full MatchData from JSON response
stock bool JSON_ParseMatchData(JSONObject json, MatchData matchData)
{
    JSON_GetStringOrDefault(json, "id", matchData.id, sizeof(MatchData::id));
    matchData.is_lan = JSON_GetBoolOrDefault(json, "is_lan");
    JSON_GetStringOrDefault(json, "password", matchData.password, sizeof(MatchData::password), "connectme");
    JSON_GetStringOrDefault(json, "current_match_map_id", matchData.current_match_map_id, sizeof(MatchData::current_match_map_id));
    JSON_GetStringOrDefault(json, "lineup_1_id", matchData.lineup_1_id, sizeof(MatchData::lineup_1_id));
    JSON_GetStringOrDefault(json, "lineup_2_id", matchData.lineup_2_id, sizeof(MatchData::lineup_2_id));

    // Parse options
    if (json.HasKey("options") && !json.IsNull("options"))
    {
        JSONObject optionsJson = view_as<JSONObject>(json.Get("options"));
        JSON_ParseMatchOptions(optionsJson, matchData.options);
        delete optionsJson;
    }

    // Parse lineups
    if (json.HasKey("lineup_1") && !json.IsNull("lineup_1"))
    {
        JSONObject lineup1Json = view_as<JSONObject>(json.Get("lineup_1"));
        JSON_ParseMatchLineUp(lineup1Json, matchData.lineup_1);
        delete lineup1Json;
    }

    if (json.HasKey("lineup_2") && !json.IsNull("lineup_2"))
    {
        JSONObject lineup2Json = view_as<JSONObject>(json.Get("lineup_2"));
        JSON_ParseMatchLineUp(lineup2Json, matchData.lineup_2);
        delete lineup2Json;
    }

    // Parse match maps
    matchData.match_map_count = 0;
    if (json.HasKey("match_maps") && !json.IsNull("match_maps"))
    {
        JSONArray mapsArray = view_as<JSONArray>(json.Get("match_maps"));
        int count = mapsArray.Length;
        if (count > MAX_MATCH_MAPS) count = MAX_MATCH_MAPS;

        for (int i = 0; i < count; i++)
        {
            JSONObject mapJson = view_as<JSONObject>(mapsArray.Get(i));
            JSON_ParseMatchMap(mapJson, matchData.match_maps[matchData.match_map_count]);
            matchData.match_map_count++;
            delete mapJson;
        }
        delete mapsArray;
    }

    return true;
}
