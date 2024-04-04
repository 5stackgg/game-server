using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.entities;
using FiveStack.enums;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("get_match", "Gets match information from api")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void get_match(CCSPlayerController? player, CommandInfo command)
    {
        GetMatch();
    }

    [ConsoleCommand("upload_demos", "upload demos")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public async void upload_demos(CCSPlayerController? player, CommandInfo command)
    {
        await UploadDemos();
    }

    [ConsoleCommand("upload_backup_round", "upload backup round")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public async void upload_backup_round(CCSPlayerController? player, CommandInfo command)
    {
        string round = command.ArgByIndex(1);

        if (round == null)
        {
            return;
        }

        await UploadBackupRound(round);
    }

    [ConsoleCommand("match_state", "Forces a match to update its current state")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void SetMatchState(CCSPlayerController? player, CommandInfo command)
    {
        UpdateMapStatus(MapStatusStringToEnum(command.ArgString));
    }

    public void UpdateMapStatus(eMapStatus status)
    {
        if (_matchData == null)
        {
            Logger.LogInformation("missing event data");
            return;
        }

        Logger.LogInformation($"Update Map Status {_currentMapStatus} -> {status}");

        switch (status)
        {
            case eMapStatus.Scheduled:
            case eMapStatus.Warmup:
                status = eMapStatus.Warmup;
                StartWarmup();
                break;
            case eMapStatus.Knife:
                if (!_matchData.knife_round)
                {
                    UpdateMapStatus(eMapStatus.Live);
                    break;
                }

                var currentMap = GetCurrentMap();
                if (currentMap == null)
                {
                    break;
                }

                if (currentMap.order == _matchData.best_of && _matchData.knife_round)
                {
                    StartKnife();
                }

                break;
            case eMapStatus.Live:
                StartLive();
                break;
            default:
                PublishMapStatus(status);
                break;
        }

        _currentMapStatus = status;
    }

    public void SetupTeamNames()
    {
        if (_matchData == null)
        {
            return;
        }

        if (_matchData.lineup_1.name != null)
        {
            SendCommands(new[] { $"mp_teamname_1 {_matchData.lineup_1.name}" });
        }

        if (_matchData.lineup_2.name != null)
        {
            SendCommands(new[] { $"mp_teamname_2 {_matchData.lineup_2.name}" });
        }
    }
}
