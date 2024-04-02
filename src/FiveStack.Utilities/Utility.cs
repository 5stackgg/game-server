using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.entities;
using FiveStack.enums;
using Microsoft.Extensions.Logging;

namespace FiveStack;

// TODO - after setting up DI move these into their proper services
public partial class FiveStackPlugin
{
    private void PublishGameEvent(string Event, Dictionary<string, object> Data)
    {
        if (_matchData == null)
        {
            return;
        }
        _redis.publish(
            $"matches:{_matchData.id}",
            new Redis.EventData<Dictionary<string, object>> { @event = Event, data = Data }
        );
    }

    private bool IsWarmup()
    {
        if (_currentMap == null)
        {
            return false;
        }
        return MapStatusStringToEnum(_currentMap.status) == eMapStatus.Warmup;
    }

    private bool IsLive()
    {
        if (_currentMap == null)
        {
            return false;
        }
        return MapStatusStringToEnum(_currentMap.status) == eMapStatus.Live;
    }

    private bool IsResetingRound()
    {
        return _resetRound != null;
    }

    private bool isOverTime()
    {
        return GetOverTimeNumber() > 0;
    }

    public int GetOverTimeNumber()
    {
        CCSGameRules? rules = _gameRules();

        if (rules == null)
        {
            return 0;
        }
        return rules.OvertimePlaying;
    }

    private bool IsKnife()
    {
        if (_currentMap == null)
        {
            return false;
        }
        return MapStatusStringToEnum(_currentMap.status) == eMapStatus.Knife;
    }

    private void Message(
        HudDestination destination,
        string message,
        CCSPlayerController? player = null
    )
    {
        if (player != null)
        {
            var parts = message.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var part in parts)
            {
                player.PrintToChat($"{part}");
            }
        }
        else if (destination == HudDestination.Console)
        {
            Server.PrintToConsole(message);
        }
        else if (destination == HudDestination.Alert || destination == HudDestination.Center)
        {
            VirtualFunctions.ClientPrintAll(destination, $" {message}", 0, 0, 0, 0);
        }
        else
        {
            var parts = message.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var part in parts)
            {
                Server.PrintToChatAll($"{part}");
            }
        }
    }

    private void SendCommands(string[] commands)
    {
        foreach (var command in commands)
        {
            Server.ExecuteCommand(command);
        }
    }

    private Guid? GetPlayerLineup(CCSPlayerController player)
    {
        MatchMember? member = GetMemberFromLineup(player);

        if (member == null)
        {
            Logger.LogInformation($"Unable to find player {player.SteamID.ToString()}");
            return null;
        }

        return member.match_lineup_id;
    }

    private MatchMember? GetMemberFromLineup(CCSPlayerController player)
    {
        if (_matchData == null)
        {
            return null;
        }

        List<MatchMember> players = _matchData
            .lineup_1.lineup_players.Concat(_matchData.lineup_2.lineup_players)
            .ToList();

        return players.Find(member =>
        {
            if (member.steam_id == null)
            {
                return member.name.StartsWith(player.PlayerName);
            }

            return member.steam_id == player.SteamID.ToString();
        });
    }

    private MatchMap? GetCurrentMap()
    {
        if (_matchData == null || _matchData.current_match_map_id == null)
        {
            return null;
        }

        return _matchData?.match_maps.FirstOrDefault(match_map =>
        {
            return match_map.id == _matchData.current_match_map_id;
        });
    }

    private CsTeam TeamNumToCSTeam(int teamNum)
    {
        switch (teamNum)
        {
            case 1:
                return CsTeam.Spectator;
            case 2:
                return CsTeam.Terrorist;
            case 3:
                return CsTeam.CounterTerrorist;
            default:
                return CsTeam.None;
        }
    }

    private string TeamNumToString(int teamNum)
    {
        switch (teamNum)
        {
            case 1:
                return "Spectator";
            case 2:
                return "TERRORIST";
            case 3:
                return "CT";
            default:
                return "None";
        }
    }

    private string CSTeamToString(CsTeam team)
    {
        switch (team)
        {
            case CsTeam.Spectator:
                return "Spectator";
            case CsTeam.Terrorist:
                return "TERRORIST";
            case CsTeam.CounterTerrorist:
                return "CT";
            default:
                return "None";
        }
    }

    private CsTeam TeamStringToCsTeam(string team)
    {
        switch (team)
        {
            case "Spectator":
                return CsTeam.Spectator;
            case "TERRORIST":
                return CsTeam.Terrorist;
            case "CT":
                return CsTeam.CounterTerrorist;
            default:
                return CsTeam.None;
        }
    }

    /*
     * HitGroup_t
     */
    private string HitGroupToString(int hitGroup)
    {
        switch (hitGroup)
        {
            case 0:
                return "Body";
            case 1:
                return "Head";
            case 2:
                return "Chest";
            case 3:
                return "Stomach";
            case 4:
                return "Left Arm";
            case 5:
                return "Right Arm";
            case 6:
                return "Left Leg";
            case 7:
                return "Right Leg";
            case 10:
                return "Gear";
            default:
                return "Unknown";
        }
    }

    private eMapStatus MapStatusStringToEnum(string state)
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
            case "TechTimeout":
                return eMapStatus.TechTimeout;
            case "Warmup":
                return eMapStatus.Warmup;
            case "Unknown":
                return eMapStatus.Unknown;
            default:
                throw new ArgumentException($"Unsupported status string: {state}");
        }
    }

    private eTimeoutSettings TimeoutSettingStringToEnum(string state)
    {
        switch (state)
        {
            case "Coach":
                return eTimeoutSettings.Coach;
            case "CoachAndPlayers":
                return eTimeoutSettings.CoachAndPlayers;
            case "Admin":
                return eTimeoutSettings.Admin;
            default:
                throw new ArgumentException($"Unsupported timeout setting: {state}");
        }
    }

    private string GetSafeMatchPrefix()
    {
        if (_matchData == null)
        {
            return "backup";
        }
        return $"{_matchData.id}".Replace("-", "");
    }

    private static string ConvertCamelToHumanReadable(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        StringBuilder result = new StringBuilder(input.Length + 10);
        result.Append(char.ToUpper(input[0]));

        for (int i = 1; i < input.Length; i++)
        {
            if (char.IsUpper(input[i]) && input[i - 1] != ' ')
            {
                result.Append(' ');
            }
            result.Append(input[i]);
        }

        return result.ToString();
    }

    private bool RestoreBackupRound(string round, bool byVote = true)
    {
        string backupRoundFile = $"{GetSafeMatchPrefix()}_round{round.PadLeft(2, '0')}.txt";

        if (!File.Exists(Path.Join(Server.GameDirectory + "/csgo/", backupRoundFile)))
        {
            return false;
        }

        SendCommands(new[] { "mp_pause_match" });

        if (byVote)
        {
            _resetRound = round;

            Message(
                HudDestination.Alert,
                $" {ChatColors.Red}Reset round to {round}, captains must accept"
            );
            return true;
        }

        LoadRound(round);

        return true;
    }

    private void LoadRound(string round)
    {
        string backupRoundFile = $"{GetSafeMatchPrefix()}_round{round.PadLeft(2, '0')}.txt";

        SendCommands(new[] { $"mp_backup_restore_load_file {backupRoundFile}" });

        Message(
            HudDestination.Alert,
            $" {ChatColors.Red}Round {round} has been restored (.resume to continue)"
        );

        PublishGameEvent(
            "restoreRound",
            new Dictionary<string, object> { { "round", round + 1 }, }
        );

        ResetRestoreBackupRound();
    }

    private void ResetRestoreBackupRound()
    {
        _resetRound = null;
        _restoreRoundVote = new Dictionary<int, bool>();
    }

    private void UpdateCurrentRound()
    {
        int roundsPlayed = 0;
        var teamManagers = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");

        foreach (var teamManager in teamManagers)
        {
            if (
                teamManager.TeamNum == (int)CsTeam.Terrorist
                || teamManager.TeamNum == (int)CsTeam.CounterTerrorist
            )
            {
                roundsPlayed += teamManager.Score;
            }
        }

        _currentRound = roundsPlayed;
    }

    private CCSGameRules? _gameRules()
    {
        try
        {
            return Utilities
                .FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .First()
                .GameRules;
        }
        catch
        {
            // do nothing
        }
        return null;
    }

    private int TotalReady()
    {
        return _readyPlayers.Count(pair => pair.Value);
    }
}
