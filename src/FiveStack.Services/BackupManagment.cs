using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class BackUpManagement
{
    private string? _resetRound;
    private Dictionary<ulong, bool> _restoreRoundVote = new Dictionary<ulong, bool>();

    private readonly GameEvents _gameEvents;
    private readonly GameServer _gameServer;
    private readonly EnvironmentService _environmentService;
    private readonly ILogger<BackUpManagement> _logger;

    public BackUpManagement(
        ILogger<BackUpManagement> logger,
        GameEvents gameEvents,
        GameServer gameServer,
        EnvironmentService environmentService
    )
    {
        _logger = logger;
        _gameEvents = gameEvents;
        _gameServer = gameServer;
        _environmentService = environmentService;
    }

    public void Setup(FiveStackMatch fiveStackMatch)
    {
        _gameServer.SendCommands(
            new[]
            {
                $"mp_maxrounds {fiveStackMatch.mr * 2}",
                $"mp_overtime_enable {fiveStackMatch.overtime}",
                $"mp_backup_round_file {MatchUtility.GetSafeMatchPrefix(fiveStackMatch)}",
            }
        );
    }

    public bool IsResttingRound()
    {
        return _resetRound != null;
    }

    public bool CheckForBackupRestore(FiveStackMatch fiveStackMatch)
    {
        string directoryPath = Path.Join(Server.GameDirectory + "/csgo/");

        string[] files = Directory.GetFiles(
            directoryPath,
            MatchUtility.GetSafeMatchPrefix(fiveStackMatch) + "*"
        );

        Regex regex = new Regex(@"(\d+)(?!.*\d)");

        int highestNumber = -1;

        foreach (string file in files)
        {
            Match match = regex.Match(Path.GetFileNameWithoutExtension(file));

            if (match.Success)
            {
                int number;
                if (int.TryParse(match.Value, out number))
                {
                    highestNumber = Math.Max(highestNumber, number);
                }
            }
        }

        int currentRound = _gameServer.GetCurrentRound();
        if (highestNumber != -1)
        {
            _logger.LogInformation(
                $"Found Backup Round File {highestNumber} and were on {currentRound}"
            );
        }

        if (highestNumber != -1 && currentRound > 0 && currentRound >= highestNumber)
        {
            // we are already live, do not restart the match accidently
            return false;
        }

        if (highestNumber > currentRound)
        {
            _logger.LogInformation("Server restarted, requires a vote to restore round");
            RestoreBackupRound(fiveStackMatch, highestNumber.ToString());
            return true;
        }

        return false;
    }

    public void SetupResetMessage(FiveStackMatch match, CCSPlayerController player)
    {
        if (player.UserId == null)
        {
            return;
        }

        int totalVoted = _restoreRoundVote.Count(pair => pair.Value);

        ulong playerId = player.SteamID;
        bool isCaptain = MatchUtility.GetMemberFromLineup(match, player)?.captain ?? false;

        if (
            isCaptain == false
            || _restoreRoundVote.ContainsKey(playerId) && _restoreRoundVote[playerId]
        )
        {
            player.PrintToCenter($"Waiting for captin [{totalVoted}/2]");
            return;
        }

        player.PrintToCenter($"Type .reset reset the round to round {_resetRound}");
    }

    public bool RestoreBackupRound(
        FiveStackMatch fiveStackMatch,
        string round,
        CCSPlayerController? player = null
    )
    {
        string backupRoundFile =
            $"{MatchUtility.GetSafeMatchPrefix(fiveStackMatch)}_round{round.PadLeft(2, '0')}.txt";

        if (!File.Exists(Path.Join(Server.GameDirectory + "/csgo/", backupRoundFile)))
        {
            return false;
        }

        _gameServer.SendCommands(new[] { "mp_pause_match" });

        if (player != null)
        {
            _resetRound = round;

            ResetRestoreBackupRound();

            _restoreRoundVote[player.SteamID] = true;

            _gameServer.Message(
                HudDestination.Alert,
                $" {ChatColors.Red}Reset round to {round}, captains must accept"
            );
            return true;
        }

        LoadRound(fiveStackMatch, round);

        return true;
    }

    public void Vote(FiveStackMatch match, CCSPlayerController player)
    {
        if (_resetRound == null)
        {
            return;
        }

        _restoreRoundVote[player.SteamID] = true;

        if (_restoreRoundVote.Count(pair => pair.Value) == 2)
        {
            LoadRound(match, _resetRound);
        }
    }

    public void VoteFailed()
    {
        _gameServer.Message(
            HudDestination.Alert,
            $" {ChatColors.Red}Captain denied request to reset round to {_resetRound}"
        );
        ResetRestoreBackupRound();
    }

    public async Task UploadBackupRound(FiveStackMatch match, string round)
    {
        string? serverId = _environmentService.GetServerId();
        string? apiPassword = _environmentService.GetServerApiPassword();

        if (serverId == null || apiPassword == null)
        {
            return;
        }

        string backupRoundFilePath = Path.Join(
            Server.GameDirectory + "/csgo/",
            $"{MatchUtility.GetSafeMatchPrefix(match)}_round{round.PadLeft(2, '0')}.txt"
        );

        if (!File.Exists(backupRoundFilePath))
        {
            return;
        }

        string endpoint =
            $"https://api.5stack.gg/server/{serverId}/match/{match.id}/{match.current_match_map_id}/backup-round/{round}";

        _logger.LogInformation($"Uploading Backup Round {endpoint}");

        using (var httpClient = new HttpClient())
        {
            using (var formData = new MultipartFormDataContent())
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    apiPassword
                );

                using (var fileStream = File.OpenRead(backupRoundFilePath))
                using (var streamContent = new StreamContent(fileStream))
                {
                    streamContent.Headers.ContentType = new MediaTypeHeaderValue(
                        "application/octet-stream"
                    );
                    formData.Add(streamContent, "file", Path.GetFileName(backupRoundFilePath));

                    var response = await httpClient.PostAsync(endpoint, formData);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("File uploaded successfully.");
                    }
                    else
                    {
                        _logger.LogError($"File upload failed. Status code: {response.StatusCode}");
                    }
                }
            }
        }
    }

    public void LoadRound(FiveStackMatch match, string round)
    {
        if (match.current_match_map_id == null)
        {
            _logger.LogWarning("unable to load road because we dont have the current map");
            return;
        }

        string backupRoundFile =
            $"{MatchUtility.GetSafeMatchPrefix(match)}_round{round.PadLeft(2, '0')}.txt";

        _gameServer.SendCommands(new[] { $"mp_backup_restore_load_file {backupRoundFile}" });

        _gameServer.Message(
            HudDestination.Alert,
            $" {ChatColors.Red}Round {round} has been restored (.resume to continue)"
        );

        _gameEvents.PublishGameEvent(
            match.id,
            "restoreRound",
            new Dictionary<string, object>
            {
                { "round", round },
                { "match_map_id", match.current_match_map_id },
            }
        );
    }

    private void ResetRestoreBackupRound()
    {
        _resetRound = null;
        _restoreRoundVote = new Dictionary<ulong, bool>();
    }
}
