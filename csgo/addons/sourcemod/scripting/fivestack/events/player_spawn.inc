/**
 * 5Stack CS:GO Plugin - Player Spawn Events
 * player_spawn -> default model application
 */

#if defined _fivestack_event_spawn_included
    #endinput
#endif
#define _fivestack_event_spawn_included

// CS:GO Source 1 model paths (not .vmdl like CS2)
#define MODEL_CT_SAS "models/player/ctm_sas.mdl"
#define MODEL_T_PHOENIX "models/player/tm_phoenix.mdl"

public Action Event_PlayerSpawn(Event event, const char[] name, bool dontBroadcast)
{
    if (!g_bMatchLoaded) return Plugin_Continue;
    if (!g_MatchData.options.default_models) return Plugin_Continue;

    int client = GetClientOfUserId(event.GetInt("userid"));
    if (!IsValidClient(client)) return Plugin_Continue;

    int team = GetClientTeam(client);

    if (team == CS_TEAM_CT)
    {
        SetEntityModel(client, MODEL_CT_SAS);
    }
    else if (team == CS_TEAM_T)
    {
        SetEntityModel(client, MODEL_T_PHOENIX);
    }

    return Plugin_Continue;
}
