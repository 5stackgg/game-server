using System.Text.Json;
using FiveStack.Entities;
using FiveStack.Enums;

namespace FiveStack.Simulator;

// MatchService and the server-only console commands: holds the match the api
// gave it, loads its map, and runs the map being played.
public sealed class SimulatedServer
{
    private const double MapLoadSeconds = 10;
    private const double MapStartDelay = 5;

    public const string RestoreRefused = "restore_round refused:";

    private readonly IMatchSource _source;
    private readonly IEventSink _sink;
    private readonly IPacer _pacer;
    private readonly Random _random;
    private readonly CancellationToken _lifetime;
    private readonly object _gate = new();

    private MatchData? _match;
    private MapRun? _run;
    private CancellationTokenSource? _runCancel;
    private bool _changingMap;
    private DateTimeOffset _lastEventTime = DateTimeOffset.MinValue;

    public SimulatedServer(
        SimulatorSettings settings,
        IMatchSource source,
        IEventSink sink,
        IPacer pacer,
        Random random,
        CancellationToken lifetime
    )
    {
        Settings = settings;
        LoadedMap = settings.StartMap;
        _source = source;
        _sink = sink;
        _pacer = pacer;
        _random = random;
        _lifetime = lifetime;
    }

    public SimulatorSettings Settings { get; }
    public string LoadedMap { get; private set; }
    public bool HoldFetches { get; set; }
    public Action? MapLoaded { get; set; }

    public MatchData? Match => _match;
    public MapRun? Run => _run;
    public MapRun? LastRun { get; private set; }
    public int Players => _run?.ConnectedPlayers ?? 0;

    public async Task RefreshMatch()
    {
        if (HoldFetches)
        {
            Log.Info("map change countdown is still running, skipping match fetch");
            return;
        }

        MatchFetch fetch;
        try
        {
            fetch = await _source.Fetch(_lifetime);
        }
        catch (Exception error) when (!_lifetime.IsCancellationRequested)
        {
            Log.Warn($"unable to fetch the current match: {error.Message}");
            return;
        }

        lock (_gate)
        {
            Apply(fetch);
        }
    }

    private void Apply(MatchFetch fetch)
    {
        if (fetch.Action == eCurrentMatchAction.Ignore)
        {
            return;
        }

        if (fetch.Action == eCurrentMatchAction.Clear || fetch.Match == null)
        {
            if (_match != null)
            {
                Log.Info($"no match assigned, clearing match {_match.id}");
            }

            StopRun();
            _match = null;
            return;
        }

        MatchData match = fetch.Match;

        if (_match?.id != match.id)
        {
            Log.Info($"new match incoming: {_match?.id.ToString() ?? "none"} -> {match.id}");
            StopRun();
        }

        _match = match;

        MatchMap? map = match.match_maps.FirstOrDefault(map =>
            map.id == match.current_match_map_id
        );
        if (map == null)
        {
            Log.Warn("match does not have a current map");
            return;
        }

        if (!IsLoaded(map.map))
        {
            ChangeMap(map.map);
            return;
        }

        if (_run?.MapId != map.id)
        {
            StopRun();

            if (
                MapRun.IsFinished(StatusOf(map.status))
                || StatusOf(map.status) == eMapStatus.WaitingForTV
            )
            {
                Log.Info($"map {map.map.name} is already {map.status}");
                return;
            }

            StartRun(match, map);
        }

        if (fetch.MatchStatus == "Live")
        {
            _run?.MatchIsLive();
        }
    }

    private static eMapStatus StatusOf(string status)
    {
        return Enum.TryParse(status, out eMapStatus parsed) ? parsed : eMapStatus.Unknown;
    }

    private void StartRun(MatchData match, MatchMap map)
    {
        Log.Info($"setting up {map.map.name} ({map.id}) for match {match.id}");

        var run = new MapRun(this, match, map, _pacer, _random);
        var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);

        _run = run;
        _runCancel = cancel;

        run.Task = Task.Run(async () =>
        {
            try
            {
                await run.Play(cancel.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                Log.Warn($"map run failed: {error}");
            }
        });

        // only once it has its task, so a reader never waits on a stand-in
        LastRun = run;
    }

    private void StopRun()
    {
        _runCancel?.Cancel();
        _runCancel = null;
        _run = null;
    }

    // Every name is a map the install ships, except one that is itself a
    // workshop id, which only the workshop can load.
    private static string LoadName(Map map)
    {
        return map.name.All(char.IsDigit) && !string.IsNullOrEmpty(map.workshop_map_id)
            ? map.workshop_map_id
            : map.name;
    }

    private bool IsLoaded(Map map)
    {
        return string.Equals(LoadedMap, LoadName(map), StringComparison.OrdinalIgnoreCase);
    }

    private void ChangeMap(Map map)
    {
        if (_changingMap)
        {
            return;
        }

        StopRun();
        _changingMap = true;

        string target = LoadName(map);
        Log.Info($"changing map {LoadedMap} -> {target}");

        _ = Task.Run(async () =>
        {
            try
            {
                await _pacer.Wait(MapLoadSeconds, _lifetime);

                lock (_gate)
                {
                    LoadedMap = target;
                    _changingMap = false;
                }

                MapLoaded?.Invoke();

                await _pacer.Wait(MapStartDelay, _lifetime);
                await RefreshMatch();
            }
            catch (OperationCanceledException) { }
        });
    }

    // Times are what the api orders and keys a map's rows by, and it keeps them
    // to the millisecond, so no two events may share one.
    public void Emit(
        Guid matchId,
        Guid mapId,
        string name,
        Dictionary<string, object> data,
        bool stamped
    )
    {
        if (stamped)
        {
            lock (_gate)
            {
                DateTimeOffset now = DateTimeOffset.FromUnixTimeMilliseconds(
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                );
                _lastEventTime = now > _lastEventTime ? now : _lastEventTime.AddMilliseconds(1);
                data["time"] = _lastEventTime;
            }
        }

        _sink.Publish(matchId, mapId, name, data);
    }

    public string Execute(string line)
    {
        var replies = new List<string>();

        foreach (
            string command in line.Split(
                [';', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            string[] parts = command.Split(' ', 2, StringSplitOptions.TrimEntries);
            string name = parts[0].ToLowerInvariant();
            string args = parts.Length > 1 ? parts[1] : "";

            string? reply = Command(name, args);
            if (!string.IsNullOrEmpty(reply))
            {
                replies.Add(reply);
            }
        }

        return string.Join("\n", replies);
    }

    private string? Command(string name, string args)
    {
        MapRun? run = _run;

        switch (name)
        {
            case "get_match":
                _ = RefreshMatch();
                return null;
            case "force_ready":
                run?.ForceReady();
                return null;
            case "skip_knife":
                run?.SkipKnife();
                return null;
            case "api_knife_switch":
                run?.ConfirmKnifeSwitch();
                return null;
            case "css_pause" or "sw_pause" or "css_tech" or "sw_tech" or "mp_pause_match":
                run?.Pause();
                return null;
            case "css_resume" or "sw_resume" or "mp_unpause_match":
                run?.Resume();
                return null;
            case "match_state":
                return MatchState(args);
            case "api_restore_round":
                if (int.TryParse(args, out int round))
                {
                    run?.RestoreRound(round);
                }
                return null;
            case "restore_round":
                return $"{RestoreRefused} the simulated server keeps no round backups";
            case "fivestack_status":
                return StatusReport();
            case "status":
                return StatusText();
            case "status_json":
                return StatusJson();
            default:
                Log.Info($"rcon: {name} {args}".TrimEnd());
                return null;
        }
    }

    private string? MatchState(string status)
    {
        switch (StatusOf(status))
        {
            case eMapStatus.Paused:
                _run?.Pause();
                break;
            case eMapStatus.Live or eMapStatus.Overtime:
                _run?.Resume();
                break;
            case eMapStatus.Warmup:
                lock (_gate)
                {
                    MatchData? match = _match;
                    MatchMap? map = match?.match_maps.FirstOrDefault(map => map.id == _run?.MapId);
                    if (match == null || map == null)
                    {
                        return "match_state refused: no match is loaded";
                    }

                    StopRun();
                    StartRun(match, map);
                    _run?.MatchIsLive();
                }
                break;
            default:
                Log.Info($"match_state {status} is not simulated");
                break;
        }

        return null;
    }

    private string StatusReport()
    {
        var lines = new List<string>
        {
            "----- 5Stack -----",
            $"Plugin Version: {Settings.PluginVersion}",
            "Plugin Runtime: simulator",
            $"Server ID: {Settings.ServerId ?? "unassigned"}",
            $"API: {Settings.ApiUrl}",
        };

        MatchData? match = _match;
        if (match == null)
        {
            lines.Add("Match: none");
        }
        else
        {
            lines.Add($"Match: {match.id}");
            lines.Add($"Map: {LoadedMap}");
            lines.Add($"Map Status: {_run?.Status ?? eMapStatus.Unknown}");
        }

        return string.Join("\n", lines);
    }

    private IEnumerable<(int UserId, string SteamId, string Name)> ConnectedPlayers()
    {
        MatchData? match = _match;
        if (match == null || _run == null)
        {
            return [];
        }

        return match
            .lineup_1.lineup_players.Concat(match.lineup_2.lineup_players)
            .Where(member => !string.IsNullOrEmpty(member.steam_id))
            .Take(_run.ConnectedPlayers)
            .Select((member, index) => (index + 2, member.steam_id!, member.name));
    }

    private string StatusText()
    {
        var lines = new List<string>
        {
            $"hostname: 5Stack.gg (simulated)",
            $"map     : {LoadedMap}",
        };

        foreach ((int userId, string steamId, string name) in ConnectedPlayers())
        {
            ulong accountId = ulong.Parse(steamId) - 76561197960265728UL;
            lines.Add(
                $"# {userId} \"{name}\" STEAM_1:{accountId % 2}:{accountId / 2} 00:00 0 0 active"
            );
        }

        return string.Join("\n", lines);
    }

    private string StatusJson()
    {
        return JsonSerializer.Serialize(
            new
            {
                server = new { map = LoadedMap },
                clients = ConnectedPlayers()
                    .Select(player => new
                    {
                        userid = player.UserId,
                        steamid64 = player.SteamId,
                        name = player.Name,
                    })
                    .ToList(),
            }
        );
    }
}
