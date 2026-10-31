/**
 * 5Stack CS:GO Plugin - Player Damage Events
 * player_hurt -> "damage" event
 */

#if defined _fivestack_event_damage_included
    #endinput
#endif
#define _fivestack_event_damage_included

public Action Event_PlayerHurt(Event event, const char[] name, bool dontBroadcast)
{
    if (!IsLive()) return Plugin_Continue;

    int victim = GetClientOfUserId(event.GetInt("userid"));
    int attacker = GetClientOfUserId(event.GetInt("attacker"));

    if (!IsValidClient(victim)) return Plugin_Continue;

    char victimSteamId[MAX_STEAMID_LENGTH];
    GetClientAuthId(victim, AuthId_SteamID64, victimSteamId, sizeof(victimSteamId));

    char attackerSteamId[MAX_STEAMID_LENGTH] = "";
    int attackerTeam = CS_TEAM_NONE;
    if (IsValidClient(attacker))
    {
        GetClientAuthId(attacker, AuthId_SteamID64, attackerSteamId, sizeof(attackerSteamId));
        attackerTeam = GetClientTeam(attacker);
    }

    int damage = event.GetInt("dmg_health");
    int damageArmor = event.GetInt("dmg_armor");
    int hitgroup = event.GetInt("hitgroup");

    char weapon[64];
    event.GetString("weapon", weapon, sizeof(weapon));

    char hitgroupStr[32];
    HitGroupToString(hitgroup, hitgroupStr, sizeof(hitgroupStr));

    char data[1024];
    FormatEx(data, sizeof(data),
        "{\"attacker_steam_id\":\"%s\",\"attacker_team\":%d,"
        "\"attacked_steam_id\":\"%s\",\"attacked_team\":%d,"
        "\"damage\":%d,\"damage_armor\":%d,"
        "\"weapon\":\"%s\",\"hitgroup\":\"%s\","
        "\"round\":%d}",
        attackerSteamId, attackerTeam,
        victimSteamId, GetClientTeam(victim),
        damage, damageArmor,
        weapon, hitgroupStr,
        GetCurrentRound());

    PublishGameEvent("damage", data);

    return Plugin_Continue;
}
