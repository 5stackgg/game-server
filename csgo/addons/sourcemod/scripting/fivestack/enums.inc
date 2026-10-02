/**
 * 5Stack CS:GO Plugin - Enums
 * Mirrors FiveStack.Enums from the CS2 plugin.
 */

#if defined _fivestack_enums_included
    #endinput
#endif
#define _fivestack_enums_included

enum eMapStatus
{
    MapStatus_Knife = 0,
    MapStatus_Live,
    MapStatus_Warmup,
    MapStatus_Paused,
    MapStatus_Scheduled,
    MapStatus_Overtime,
    MapStatus_UploadingDemo,
    MapStatus_Finished,
    MapStatus_Surrendered,
    MapStatus_Unknown
};

enum eReadySettings
{
    ReadySetting_Captains = 0,
    ReadySetting_Coach,
    ReadySetting_Admin,
    ReadySetting_Players
};

enum eTimeoutSettings
{
    TimeoutSetting_Coach = 0,
    TimeoutSetting_CoachAndCaptains,
    TimeoutSetting_CoachAndPlayers,
    TimeoutSetting_Admin
};

enum ePlayerRoles
{
    PlayerRole_User = 0,
    PlayerRole_VerifiedUser,
    PlayerRole_Streamer,
    PlayerRole_MatchOrganizer,
    PlayerRole_TournamentOrganizer,
    PlayerRole_Administrator
};

enum eWinReason
{
    WinReason_TerroristsWin = 0,
    WinReason_CTsWin,
    WinReason_BombExploded,
    WinReason_TimeRanOut,
    WinReason_BombDefused,
    WinReason_Unknown
};

stock eMapStatus MapStatusStringToEnum(const char[] state)
{
    if (StrEqual(state, "Scheduled")) return MapStatus_Scheduled;
    if (StrEqual(state, "Finished")) return MapStatus_Finished;
    if (StrEqual(state, "Knife")) return MapStatus_Knife;
    if (StrEqual(state, "Live")) return MapStatus_Live;
    if (StrEqual(state, "Overtime")) return MapStatus_Overtime;
    if (StrEqual(state, "Paused")) return MapStatus_Paused;
    if (StrEqual(state, "Warmup")) return MapStatus_Warmup;
    if (StrEqual(state, "UploadingDemo")) return MapStatus_UploadingDemo;
    if (StrEqual(state, "Surrendered")) return MapStatus_Surrendered;
    return MapStatus_Unknown;
}

stock void MapStatusToString(eMapStatus status, char[] buffer, int maxlen)
{
    switch (status)
    {
        case MapStatus_Knife:          strcopy(buffer, maxlen, "Knife");
        case MapStatus_Live:           strcopy(buffer, maxlen, "Live");
        case MapStatus_Warmup:         strcopy(buffer, maxlen, "Warmup");
        case MapStatus_Paused:         strcopy(buffer, maxlen, "Paused");
        case MapStatus_Scheduled:      strcopy(buffer, maxlen, "Scheduled");
        case MapStatus_Overtime:       strcopy(buffer, maxlen, "Overtime");
        case MapStatus_UploadingDemo:  strcopy(buffer, maxlen, "UploadingDemo");
        case MapStatus_Finished:       strcopy(buffer, maxlen, "Finished");
        case MapStatus_Surrendered:    strcopy(buffer, maxlen, "Surrendered");
        default:                       strcopy(buffer, maxlen, "Unknown");
    }
}

stock eReadySettings ReadySettingStringToEnum(const char[] setting)
{
    if (StrEqual(setting, "Captains")) return ReadySetting_Captains;
    if (StrEqual(setting, "Coach")) return ReadySetting_Coach;
    if (StrEqual(setting, "Admin")) return ReadySetting_Admin;
    return ReadySetting_Players;
}

stock eTimeoutSettings TimeoutSettingStringToEnum(const char[] setting)
{
    if (StrEqual(setting, "Coach")) return TimeoutSetting_Coach;
    if (StrEqual(setting, "CoachAndCaptains")) return TimeoutSetting_CoachAndCaptains;
    if (StrEqual(setting, "CoachAndPlayers")) return TimeoutSetting_CoachAndPlayers;
    if (StrEqual(setting, "Admin")) return TimeoutSetting_Admin;
    return TimeoutSetting_CoachAndPlayers;
}

stock ePlayerRoles PlayerRoleStringToEnum(const char[] role)
{
    if (StrEqual(role, "administrator")) return PlayerRole_Administrator;
    if (StrEqual(role, "match_organizer")) return PlayerRole_MatchOrganizer;
    if (StrEqual(role, "tournament_organizer")) return PlayerRole_TournamentOrganizer;
    if (StrEqual(role, "streamer")) return PlayerRole_Streamer;
    if (StrEqual(role, "verified_user")) return PlayerRole_VerifiedUser;
    return PlayerRole_User;
}

stock eWinReason WinReasonFromMessage(const char[] message)
{
    if (StrEqual(message, "#SFUI_Notice_Terrorists_Win")) return WinReason_TerroristsWin;
    if (StrEqual(message, "#SFUI_Notice_CTs_Win")) return WinReason_CTsWin;
    if (StrEqual(message, "#SFUI_Notice_Target_Bombed")) return WinReason_BombExploded;
    if (StrEqual(message, "#SFUI_Notice_Target_Saved")) return WinReason_TimeRanOut;
    if (StrEqual(message, "#SFUI_Notice_Bomb_Defused")) return WinReason_BombDefused;
    return WinReason_Unknown;
}

stock void WinReasonToString(eWinReason reason, char[] buffer, int maxlen)
{
    switch (reason)
    {
        case WinReason_TerroristsWin:  strcopy(buffer, maxlen, "TerroristsWin");
        case WinReason_CTsWin:         strcopy(buffer, maxlen, "CTsWin");
        case WinReason_BombExploded:   strcopy(buffer, maxlen, "BombExploded");
        case WinReason_TimeRanOut:     strcopy(buffer, maxlen, "TimeRanOut");
        case WinReason_BombDefused:    strcopy(buffer, maxlen, "BombDefused");
        default:                       strcopy(buffer, maxlen, "Unknown");
    }
}
