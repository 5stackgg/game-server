using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace FiveStack;

public class GameBackUpRounds
{
    private string? _resetRound;
    private Timer? _resetRoundTimer;
    private bool _initialRestore = false;
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
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

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
        if (File.Exists("/opt/initial-restore.lock"))
        {
            return false;
        }

        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

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
            _initialRestore = true;
            _logger.LogInformation("Server restarted, requires a vote to restore round");
            RestoreBackupRound(highestNumber.ToString(), null, true);
            return true;
        }

        return false;
    }

    public async Task DownloadBackupRounds()
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();
        if (match == null)
        {
            return;
        }

        string? serverId = _environmentService.GetServerId();
        string? apiPassword = _environmentService.GetServerApiPassword();

        string endpoint =
            $"https://api.5stack.gg/matches/{match.id}/backup-rounds/map/{match.current_match_map_id}";

        string downloadDirectory = "/opt";
        Directory.CreateDirectory(downloadDirectory);

        string zipFilePath = Path.Combine(downloadDirectory, "backup-rounds.zip");

        if (File.Exists(zipFilePath))
        {
            return;
        }

        _logger.LogInformation($"Downloading Backup Rounds {endpoint}");

        using (HttpClient httpClient = new HttpClient())
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                apiPassword
            );

            try
            {
                HttpResponseMessage response = await httpClient.GetAsync(endpoint);

                if (response.IsSuccessStatusCode)
                {
                    using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                    {
                        using (
                            FileStream fileStream = new FileStream(
                                zipFilePath,
                                FileMode.Create,
                                FileAccess.Write,
                                FileShare.None
                            )
                        )
                        {
                            await contentStream.CopyToAsync(fileStream);
                        }
                    }
                    _logger.LogTrace($"backup rounds downloaded: {zipFilePath}");
                }
                else if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return;
                }
                else
                {
                    _logger.LogError($"backup rounds failed to download: {response.StatusCode}");
                    return;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError($"backup rounds failed to download: {ex.Message}");
                return;
            }
        }

        await Server.NextFrameAsync(() =>
        {
            string extractPath = Path.Join(Server.GameDirectory + "/csgo/");
            try
            {
                ZipFile.ExtractToDirectory(zipFilePath, extractPath, true);
                _logger.LogInformation($"backup rounds downloaded");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error extracting backup rounds zip: {ex.Message}");
            }
        });
    }

    public void SetupResetMessage(CCSPlayerController player)
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

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
            player.PrintToCenter(
                $"Waiting for captins [{totalVoted}/2] to reset round to {_resetRound}"
            );
            return;
        }

        player.PrintToCenter($"Type .yes / .no reset the round to round {_resetRound}");
    }

    public void CastVote(CCSPlayerController player, bool vote)
    {
        if (_resetRound == null)
        {
            return;
        }

        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();

        if (matchData == null)
        {
            return;
        }

        if (MatchUtility.GetMemberFromLineup(matchData, player)?.captain == true)
        {
            if (vote == false)
            {
                VoteFailed();
                return;
            }

            _restoreRoundVote[player.SteamID] = true;

            if (_restoreRoundVote.Count(pair => pair.Value) == 2)
            {
                RestoreRound(_resetRound);
                return;
            }

            SendResetRoundMessage();
        }
    }

    public bool RestoreBackupRound(
        string round,
        CCSPlayerController? player = null,
        bool vote = false
    )
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (match == null || matchData == null)
        {
            return false;
        }

        string backupRoundFile =
            $"{MatchUtility.GetSafeMatchPrefix(matchData)}_round{round.PadLeft(2, '0')}.txt";

        if (!File.Exists(Path.Join(Server.GameDirectory + "/csgo/", backupRoundFile)))
        {
            return false;
        }

        Server.NextFrame(() =>
        {
            match.PauseMatch();
        });

        if (player != null || vote == true)
        {
            _resetRound = round;
            if (_resetRoundTimer == null)
            {
                _resetRoundTimer = TimerUtility.AddTimer(
                    3,
                    SendResetRoundMessage,
                    TimerFlags.REPEAT
                );
            }

            if (player != null)
            {
                _restoreRoundVote[player.SteamID] = true;
            }

            _gameServer.Message(
                HudDestination.Alert,
                $" {ChatColors.Red}Reset round to {round}, captains must accept"
            );
            return true;
        }
        RestoreRound(round);

        return true;
    }

    public void VoteFailed()
    {
        if (_initialRestore)
        {
            File.Create("/opt/initial-restore.lock").Close();
        }

        _gameServer.Message(
            HudDestination.Alert,
            $" {ChatColors.Red}Captain denied request to reset round to {_resetRound}"
        );
        ResetRestoreBackupRound();
    }

    public async Task UploadBackupRound(string round)
    {
        try
        {
            MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();
            if (match == null)
            {
                return;
            }

            string? serverId = _environmentService.GetServerId();
            string? apiPassword = _environmentService.GetServerApiPassword();

            if (serverId == null || apiPassword == null)
            {
                _logger.LogInformation(
                    $"Unable to upload backup round because we're missing server id / api password"
                );
                return;
            }

            string backupRoundFilePath = Path.Join(
                Server.GameDirectory + "/csgo/",
                $"{MatchUtility.GetSafeMatchPrefix(match)}_round{round.PadLeft(2, '0')}.txt"
            );

            if (!File.Exists(backupRoundFilePath))
            {
                _logger.LogInformation(
                    $"Unable to upload backup round because it's missing {backupRoundFilePath}"
                );
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
                            _logger.LogInformation("backup round uploaded");
                        }
                        else
                        {
                            _logger.LogError(
                                $"unable to upload backup round {response.StatusCode}"
                            );
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"An error occurred during backup round upload: {ex.Message}");
        }
    }

    public void RestoreRound(string round)
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();
        if (match?.current_match_map_id == null)
        {
            return;
        }

        _gameEvents.PublishGameEvent(
            "restoreRound",
            new Dictionary<string, object>
            {
                { "round", round },
                { "match_map_id", match.current_match_map_id },
            }
        );
    }

    public void ResetRestoreBackupRound()
    {
        _resetRound = null;
        _resetRoundTimer?.Kill();
        _resetRoundTimer = null;
        _restoreRoundVote = new Dictionary<ulong, bool>();
    }

    private void SendResetRoundMessage()
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null)
        {
            return;
        }

        if (!IsResttingRound())
        {
            _resetRoundTimer?.Kill();
            _resetRoundTimer = null;
            return;
        }

        foreach (var player in CounterStrikeSharp.API.Utilities.GetPlayers())
        {
            SetupResetMessage(player);
        }
    }
}
