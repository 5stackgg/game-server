/**
 * =============================================================================
 * 5Stack CS:GO SourceMod Plugin
 * =============================================================================
 *
 * 5Stack creates and manages custom competitive matches.
 * This is the CS:GO (Source 1) equivalent of the CS2 CounterStrikeSharp plugin.
 *
 * Requires: sm-ripext (JSON + HTTP)
 *
 * Author: 5Stack.gg
 * URL: https://5stack.gg
 * =============================================================================
 */

#pragma semicolon 1
#pragma newdecls required

#include <sourcemod>
#include <sdktools>
#include <cstrike>
#include <ripext>

// Core includes (order matters — dependencies must come first)
#include "fivestack/enums.inc"
#include "fivestack/match_data.inc"
#include "fivestack/config.inc"
#include "fivestack/json_helpers.inc"
#include "fivestack/globals.inc"
#include "fivestack/http_client.inc"
#include "fivestack/game_server.inc"
#include "fivestack/team_utility.inc"
#include "fivestack/match_utility.inc"

// Systems
#include "fivestack/match_events.inc"
#include "fivestack/game_demos.inc"
#include "fivestack/vote_system.inc"
#include "fivestack/captain_system.inc"
#include "fivestack/ready_system.inc"
#include "fivestack/knife_system.inc"
#include "fivestack/timeout_system.inc"
#include "fivestack/match_manager.inc"
#include "fivestack/match_service.inc"
#include "fivestack/player_auth.inc"

// Event handlers
#include "fivestack/events/player_kills.inc"
#include "fivestack/events/player_damage.inc"
#include "fivestack/events/player_utility.inc"
#include "fivestack/events/player_connected.inc"
#include "fivestack/events/player_disconnected.inc"
#include "fivestack/events/player_chat.inc"
#include "fivestack/events/player_spawn.inc"
#include "fivestack/events/round_start.inc"
#include "fivestack/events/round_end.inc"
#include "fivestack/events/bomb.inc"
#include "fivestack/events/game_end.inc"

// Commands
#include "fivestack/commands/ready_cmd.inc"
#include "fivestack/commands/knife_cmd.inc"
#include "fivestack/commands/captain_cmd.inc"
#include "fivestack/commands/timeout_cmd.inc"
#include "fivestack/commands/vote_cmd.inc"
#include "fivestack/commands/match_cmd.inc"
#include "fivestack/commands/demo_cmd.inc"
#include "fivestack/commands/help_cmd.inc"

public Plugin myinfo =
{
    name        = "5Stack",
    author      = "5Stack.gg",
    description = "5Stack creates and manages custom matches",
    version     = FIVESTACK_VERSION,
    url         = "https://5stack.gg"
};

public void OnPluginStart()
{
    LogMessage("[5Stack] Plugin v%s loading...", FIVESTACK_VERSION);

    // Initialize config ConVars
    Config_Init();

    // Initialize global state
    Globals_Init();

    // Register chat commands (. prefix via say hook)
    RegConsoleCmd("sm_ready", Command_Ready, "Toggle ready status");
    RegConsoleCmd("sm_r", Command_Ready, "Toggle ready status");
    RegConsoleCmd("sm_unready", Command_Ready, "Toggle ready status");
    RegConsoleCmd("sm_ur", Command_Ready, "Toggle ready status");

    RegConsoleCmd("sm_stay", Command_Stay, "Stay on current side after knife");
    RegConsoleCmd("sm_switch", Command_Switch, "Switch sides after knife");
    RegConsoleCmd("sm_swap", Command_Switch, "Switch sides after knife");
    RegConsoleCmd("sm_t", Command_PickT, "Pick T side after knife");
    RegConsoleCmd("sm_ct", Command_PickCT, "Pick CT side after knife");

    RegConsoleCmd("sm_captain", Command_Captain, "Claim captain");
    RegConsoleCmd("sm_captains", Command_Captains, "Show captains");
    RegConsoleCmd("sm_release-captain", Command_ReleaseCaptain, "Release captain");

    RegConsoleCmd("sm_pause", Command_Pause, "Request technical pause");
    RegConsoleCmd("sm_tech", Command_Pause, "Request technical pause");
    RegConsoleCmd("sm_p", Command_Pause, "Request technical pause");
    RegConsoleCmd("sm_resume", Command_Resume, "Request resume");
    RegConsoleCmd("sm_unpause", Command_Resume, "Request resume");
    RegConsoleCmd("sm_up", Command_Resume, "Request resume");
    RegConsoleCmd("sm_timeout", Command_TacTimeout, "Call tactical timeout");
    RegConsoleCmd("sm_tac", Command_TacTimeout, "Call tactical timeout");

    RegConsoleCmd("sm_y", Command_VoteYes, "Vote yes");
    RegConsoleCmd("sm_n", Command_VoteNo, "Vote no");

    RegConsoleCmd("sm_help", Command_Help, "Show available commands");

    // Server-only commands
    RegServerCmd("get_match", Command_GetMatch, "Fetch match data from API");
    RegServerCmd("match_state", Command_MatchState, "Show current match state");
    RegServerCmd("force_ready", Command_ForceReady, "Force start match");
    RegServerCmd("skip_knife", Command_SkipKnife, "Skip knife round");
    RegServerCmd("upload_demos", Command_UploadDemos, "Upload demos");
    RegServerCmd("test_start_demo", Command_TestStartDemo, "Test start demo recording");
    RegServerCmd("test_stop_demo", Command_TestStopDemo, "Test stop demo recording");
    RegServerCmd("sm_fivestack_allow", Command_FiveStackAllow, "Pre-authorize a player");

    // Hook game events
    HookEvent("player_death", Event_PlayerDeath);
    HookEvent("player_hurt", Event_PlayerHurt);
    HookEvent("player_spawn", Event_PlayerSpawn);
    HookEvent("player_connect_full", Event_PlayerConnectFull);
    HookEvent("player_disconnect", Event_PlayerDisconnect);
    HookEvent("round_start", Event_RoundStart);
    HookEvent("round_end", Event_RoundEnd);
    HookEvent("round_officially_ended", Event_RoundOfficiallyEnded);
    HookEvent("bomb_planted", Event_BombPlanted);
    HookEvent("bomb_defused", Event_BombDefused);
    HookEvent("bomb_exploded", Event_BombExploded);
    HookEvent("cs_win_panel_match", Event_CSWinPanelMatch);

    // Grenade events
    HookEvent("decoy_detonate", Event_DecoyDetonate);
    HookEvent("hegrenade_detonate", Event_HEGrenadeDetonate);
    HookEvent("flashbang_detonate", Event_FlashbangDetonate);
    HookEvent("molotov_detonate", Event_MolotovDetonate);
    HookEvent("smokegrenade_detonate", Event_SmokeDetonate);
    HookEvent("player_blind", Event_PlayerBlind);

    // Hook say commands for chat events and dot-prefix commands
    AddCommandListener(Listener_Say, "say");
    AddCommandListener(Listener_Say, "say_team");

    // Precache models
    PrecacheModel(MODEL_CT_SAS, true);
    PrecacheModel(MODEL_T_PHOENIX, true);

    LogMessage("[5Stack] Plugin loaded successfully");
}

public void OnConfigsExecuted()
{
    // Load config values after exec
    Config_Load();

    if (!Config_IsValid())
    {
        LogError("[5Stack] Config invalid: SERVER_ID or API_PASSWORD not set. Waiting for config...");
        return;
    }

    LogMessage("[5Stack] Config loaded — Server ID: %s", g_szServerId);

    // Start ping timer
    Ping_Start();

    // Start event retry timer
    MatchEvents_Init();

    // Fetch match data
    MatchService_FetchMatch();
}

public void OnMapStart()
{
    PrecacheModel(MODEL_CT_SAS, true);
    PrecacheModel(MODEL_T_PHOENIX, true);
}

public void OnMapEnd()
{
    // Reset match state on map change
    MatchManager_Reset();
}

// Handle say commands with . prefix
public Action Listener_Say(int client, const char[] command, int argc)
{
    if (!IsValidClient(client))
        return Plugin_Continue;

    char text[256];
    GetCmdArgString(text, sizeof(text));

    // Remove surrounding quotes
    StripQuotes(text);
    TrimString(text);

    if (text[0] == '\0')
        return Plugin_Continue;

    // Handle dot-prefix commands
    if (text[0] == '.')
    {
        char cmdText[64];
        strcopy(cmdText, sizeof(cmdText), text[1]); // skip the dot

        // Trim and lowercase
        TrimString(cmdText);

        if (StrEqual(cmdText, "ready", false) || StrEqual(cmdText, "r", false) ||
            StrEqual(cmdText, "unready", false) || StrEqual(cmdText, "ur", false))
        {
            Ready_Toggle(client);
            return Plugin_Handled;
        }
        else if (StrEqual(cmdText, "stay", false))
        {
            Knife_Stay(client);
            return Plugin_Handled;
        }
        else if (StrEqual(cmdText, "switch", false) || StrEqual(cmdText, "swap", false))
        {
            Knife_Switch(client);
            return Plugin_Handled;
        }
        else if (StrEqual(cmdText, "t", false))
        {
            Knife_PickSide(client, CS_TEAM_T);
            return Plugin_Handled;
        }
        else if (StrEqual(cmdText, "ct", false))
        {
            Knife_PickSide(client, CS_TEAM_CT);
            return Plugin_Handled;
        }
        else if (StrEqual(cmdText, "captain", false))
        {
            Captain_Claim(client);
            return Plugin_Handled;
        }
        else if (StrEqual(cmdText, "captains", false))
        {
            Captain_ShowCaptains();
            return Plugin_Handled;
        }
        else if (StrEqual(cmdText, "release-captain", false))
        {
            Captain_Release(client);
            return Plugin_Handled;
        }
        else if (StrEqual(cmdText, "pause", false) || StrEqual(cmdText, "tech", false) ||
                 StrEqual(cmdText, "p", false))
        {
            Timeout_RequestPause(client);
            return Plugin_Handled;
        }
        else if (StrEqual(cmdText, "resume", false) || StrEqual(cmdText, "unpause", false) ||
                 StrEqual(cmdText, "up", false))
        {
            Timeout_RequestResume(client);
            return Plugin_Handled;
        }
        else if (StrEqual(cmdText, "timeout", false) || StrEqual(cmdText, "tac", false))
        {
            Timeout_CallTactical(client);
            return Plugin_Handled;
        }
        else if (StrEqual(cmdText, "y", false) || StrEqual(cmdText, "yes", false))
        {
            Vote_Cast(client, true);
            return Plugin_Handled;
        }
        else if (StrEqual(cmdText, "n", false) || StrEqual(cmdText, "no", false))
        {
            Vote_Cast(client, false);
            return Plugin_Handled;
        }
        else if (StrEqual(cmdText, "help", false) || StrEqual(cmdText, "rules", false))
        {
            Command_Help(client, 0);
            return Plugin_Handled;
        }
    }

    // Process chat event (gag check + publish)
    return HandleClientChat(client, command, text);
}
