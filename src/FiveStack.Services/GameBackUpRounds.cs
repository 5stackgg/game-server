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
    private int? _resetRound;
    private Timer? _resetRoundTimer;
    private bool _initialRestore = false;
    private Dictionary<ulong, bool> _restoreRoundVote = new Dictionary<ulong, bool>();

    private readonly MatchEvents _matchEvents;
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly EnvironmentService _environmentService;
    private readonly ILogger<GameBackUpRounds> _logger;

    private string _rootDir = "/opt";

    public GameBackUpRounds(
        ILogger<GameBackUpRounds> logger,
        MatchEvents matchEvents,
        GameServer gameServer,
        MatchService matchService,
        EnvironmentService environmentService
    )
    {
        _logger = logger;
        _matchEvents = matchEvents;
        _gameServer = gameServer;
        _matchService = matchService;
        _environmentService = environmentService;

        if (
            !Directory.Exists(_rootDir)
            || new DirectoryInfo(_rootDir).Attributes.HasFlag(FileAttributes.ReadOnly)
        )
        {
            _rootDir = Server.GameDirectory;
        }
    }

    public void Setup()
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match == null)
        {
            return;
        }

        string lockFilePath = GetLockFilePath(match.id);
        if (File.Exists(lockFilePath))
        {
            return;
        }

        File.Create(lockFilePath).Dispose();

        _gameServer.SendCommands(
            new[] { $"mp_backup_round_file {MatchUtility.GetSafeMatchPrefix(match)}" }
        );
    }

    public bool IsResettingRound()
    {
        return _resetRound != null;
    }

    public bool CheckForBackupRestore()
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match == null)
        {
            return false;
        }

        if (File.Exists(GetMatchLockFile()))
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
            RestoreBackupRound(highestNumber, null, true);
            return true;
        }

        return false;
    }

    public async Task DownloadBackupRounds()
    {
        if (File.Exists(GetMatchDownloadLockFile()))
        {
            return;
        }

        File.Create(GetMatchDownloadLockFile()).Close();

        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();
        if (match == null)
        {
            return;
        }

        string? serverId = _environmentService.GetServerId();
        string? apiPassword = _environmentService.GetServerApiPassword();

        string endpoint =
            $"{_environmentService.GetApiUrl()}/matches/{match.id}/backup-rounds/map/{match.current_match_map_id}";

        Directory.CreateDirectory(_rootDir);

        string zipFilePath = Path.Combine(_rootDir, "backup-rounds.zip");

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
                    var contentType = response.Content.Headers.ContentType;
                    if (contentType?.MediaType == "text/html")
                    {
                        _logger.LogInformation("Backup rounds are empty");
                        return;
                    }

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

        player.PrintToCenter(
            $"Type {CommandUtility.PublicChatTrigger}y / {CommandUtility.PublicChatTrigger}n reset the round to round {_resetRound}"
        );
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
                RestoreRound(_resetRound ?? 0);
                return;
            }

            SendResetRoundMessage();
        }
    }

    public bool RestoreBackupRound(int round, CCSPlayerController? player = null, bool vote = false)
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (match == null || matchData == null)
        {
            return false;
        }

        if (!HasBackupRound(round))
        {
            return false;
        }

        string backupRoundFile =
            $"{MatchUtility.GetSafeMatchPrefix(matchData)}_round{round.ToString().PadLeft(2, '0')}.txt";

        Server.NextFrame(() =>
        {
            _matchService.GetCurrentMatch()?.PauseMatch();
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

            SendResetRoundMessage();

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
            File.Create(GetMatchLockFile()).Close();
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
                $"{_environmentService.GetApiUrl()}/matches/{match.id}/backup-rounds/map/{match.current_match_map_id}/round/{round}";

            _logger.LogInformation($"Uploading Backup Round {endpoint}");

            using (var httpClient = new HttpClient())
            using (var fileStream = File.OpenRead(backupRoundFilePath))
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    apiPassword
                );

                using (var formData = new MultipartFormDataContent())
                using (var streamContent = new StreamContent(fileStream))
                {
                    streamContent.Headers.ContentType = new MediaTypeHeaderValue(
                        "application/octet-stream"
                    );
                    formData.Add(streamContent, "file", Path.GetFileName(backupRoundFilePath));

                    var response = await httpClient.PostAsync(endpoint, formData);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError($"unable to upload backup round {response.StatusCode}");
                    }
                    else
                    {
                        _logger.LogInformation("backup round uploaded");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"An error occurred during backup round upload: {ex.Message}");
        }
    }

    public void RestoreRound(int round)
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();
        if (match?.current_match_map_id == null)
        {
            return;
        }

        _matchEvents.PublishGameEvent(
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

    public bool HasBackupRound(int round)
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();
        if (match?.current_match_map_id == null)
        {
            return false;
        }

        string backupRoundFile =
            $"{MatchUtility.GetSafeMatchPrefix(match)}_round{round.ToString().PadLeft(2, '0')}.txt";

        return File.Exists(Path.Join(Server.GameDirectory + "/csgo/", backupRoundFile));
    }

    private string GetMatchLockFile()
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match == null)
        {
            return $"{_rootDir}/initial-restore.lock";
        }

        return $"{_rootDir}/initial-restore-{match.id}.lock";
    }

    private string GetMatchDownloadLockFile()
    {
        MatchData? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match == null)
        {
            return $"{_rootDir}/download.lock";
        }

        return $"{_rootDir}/download-{match.id}.lock";
    }

    private void SendResetRoundMessage()
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null)
        {
            return;
        }

        if (!IsResettingRound())
        {
            _resetRoundTimer?.Kill();
            _resetRoundTimer = null;
            return;
        }

        foreach (var player in MatchUtility.Players())
        {
            if (player.IsBot)
            {
                continue;
            }
            SetupResetMessage(player);
        }
    }

    private string GetLockFilePath(Guid matchId)
    {
        return $"{_rootDir}/.backup-rounds-{matchId}";
    }
}
