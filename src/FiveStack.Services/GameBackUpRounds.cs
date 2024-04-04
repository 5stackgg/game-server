using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class GameBackUpRounds
{
    private string? _resetRound;
    private Dictionary<ulong, bool> _restoreRoundVote = new Dictionary<ulong, bool>();

    private readonly MatchEvents _gameEvents;
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly EnvironmentService _environmentService;
    private readonly ILogger<GameBackUpRounds> _logger;

    public GameBackUpRounds(
        ILogger<GameBackUpRounds> logger,
        MatchEvents gameEvents,
        GameServer gameServer,
        MatchService matchService,
        EnvironmentService environmentService
    )
    {
        _logger = logger;
        _gameEvents = gameEvents;
        _gameServer = gameServer;
        _matchService = matchService;
        _environmentService = environmentService;
    }

    public void Setup()
    {
        FiveStackMatch? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match == null)
        {
            return;
        }

        _gameServer.SendCommands(
            new[]
            {
                $"mp_maxrounds {match.mr * 2}",
                $"mp_overtime_enable {match.overtime}",
                $"mp_backup_round_file {MatchUtility.GetSafeMatchPrefix(match)}",
            }
        );
    }

    public bool IsResttingRound()
    {
        return _resetRound != null;
    }

    public bool CheckForBackupRestore()
    {
        FiveStackMatch? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match == null)
        {
            return false;
        }

        string directoryPath = Path.Join(Server.GameDirectory + "/csgo/");

        string[] files = Directory.GetFiles(
            directoryPath,
            MatchUtility.GetSafeMatchPrefix(match) + "*"
        );

        Regex regex = new Regex(@"(\d+)(?!.*\d)");

        int highestNumber = -1;

        foreach (string file in files)
        {
            Match isMatched = regex.Match(Path.GetFileNameWithoutExtension(file));

            if (isMatched.Success)
            {
                int number;
                if (int.TryParse(isMatched.Value, out number))
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
            RestoreBackupRound(highestNumber.ToString());
            return true;
        }

        return false;
    }

    public void SetupResetMessage(CCSPlayerController player)
    {
        FiveStackMatch? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match == null || player.UserId == null)
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

    public void CastVote(CCSPlayerController player, string? vote)
    {
        if (_resetRound == null)
        {
            return;
        }

        FiveStackMatch? matchData = _matchService.GetCurrentMatch()?.GetMatchData();

        if (matchData == null)
        {
            return;
        }

        if (MatchUtility.GetMemberFromLineup(matchData, player)?.captain == true)
        {
            // TODO - different command to progress failure?
            // mabye just a .y / .n
            if (vote != null)
            {
                VoteFailed();
                return;
            }

            _restoreRoundVote[player.SteamID] = true;

            if (_restoreRoundVote.Count(pair => pair.Value) == 2)
            {
                LoadRound(_resetRound);
            }
        }
    }

    public bool RestoreBackupRound(string round, CCSPlayerController? player = null)
    {
        FiveStackMatch? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match == null)
        {
            return false;
        }

        string backupRoundFile =
            $"{MatchUtility.GetSafeMatchPrefix(match)}_round{round.PadLeft(2, '0')}.txt";

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

        LoadRound(round);

        return true;
    }

    public void VoteFailed()
    {
        _gameServer.Message(
            HudDestination.Alert,
            $" {ChatColors.Red}Captain denied request to reset round to {_resetRound}"
        );
        ResetRestoreBackupRound();
    }

    public async Task UploadBackupRound(string round)
    {
        FiveStackMatch? match = _matchService.GetCurrentMatch()?.GetMatchData();
        if (match == null)
        {
            return;
        }

        string? serverId = _environmentService.GetServerId();
        string? apiPassword = _environmentService.GetServerApiPassword();

        if (serverId == null || apiPassword == null)
        {
            _logger.LogInformation($"unable to upload backup round because were missing server id / api password");
            return;
        }

        string backupRoundFilePath = Path.Join(
            Server.GameDirectory + "/csgo/",
            $"{MatchUtility.GetSafeMatchPrefix(match)}_round{round.PadLeft(2, '0')}.txt"
        );

        if (!File.Exists(backupRoundFilePath))
        {
            _logger.LogInformation($"unable to upload backup round because its missing {backupRoundFilePath}");
            return;
        }

        string endpoint =
            $"{_environmentService.GetBaseUri()}/matches/{match.id}/backup-rounds/map/{match.current_match_map_id}/round/{round}";

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

    public void LoadRound(string round)
    {
        FiveStackMatch? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match?.current_match_map_id == null)
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
