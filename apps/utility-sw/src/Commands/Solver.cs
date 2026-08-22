using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Trace;
using static SwiftlyS2.Shared.Helper;

namespace UtilityPractice;

// The solver's surface: one verb for the panel to drive over RCON, one for a
// player standing where they want to throw from, and one to inspect the gate
// without spending three hundred grenades finding out it is shut.
public partial class UtilityPracticePlugin
{
    [Command("utility_solver_calibrate", registerRaw: true, permission: "")]
    public void OnSolverCalibrate(ICommandContext context)
    {
        bool force = context.Args.Any(argument =>
            argument.Equals("force", StringComparison.OrdinalIgnoreCase)
        );

        string map = _library.Map;
        CalibrationReport? cached = _solver.CalibrationFor(map);

        if (cached != null && !force && !ShouldRetry(cached))
        {
            ReportCalibration(context, cached, cached: true);
            return;
        }

        if (force)
        {
            _solver.Forget(map);
        }

        Reply(context, $" {ChatColors.Grey}calibrating the solver on {map}...");

        if (!_solver.Calibrate(map, CalibrationSamples(), report => ReportCalibration(context, report, cached: false)))
        {
            Reply(context, $" {ChatColors.Red}{_solver.BusyWith} is already running");
        }
    }

    // Named arguments, because this one is called by a machine over RCON where
    // a positional list quietly solves for the wrong point.
    [Command("utility_solver_solve", registerRaw: true, permission: "")]
    public void OnSolverSolve(ICommandContext context)
    {
        if (
            context.Args.Length == 1
            && context.Args[0].Equals("cancel", StringComparison.OrdinalIgnoreCase)
        )
        {
            Reply(
                context,
                _solver.Cancel()
                    ? $" {ChatColors.Green}solve cancelled"
                    : $" {ChatColors.Red}nothing is solving"
            );
            return;
        }

        if (!PracticeSolverUtility.TryParse(context.Args, out SolveRequest request, out string error))
        {
            Reply(context, $" {ChatColors.Red}{error}");
            Reply(
                context,
                $" {ChatColors.Grey}usage: utility_solver_solve target=x,y,z [from=x,y,z] [utility=Smoke] [steam=id] [name=...] [tolerance=40] [grenades=300] [seconds=120]"
            );
            return;
        }

        if (request.eye.Length() <= 0f)
        {
            IPlayer? sender = context.Sender;
            ThrowSnapshot? standing = sender == null ? null : PracticeSystem.Where(sender);

            if (sender == null || standing == null)
            {
                Reply(
                    context,
                    $" {ChatColors.Red}from=x,y,z is required when the caller is not standing in the map"
                );
                return;
            }

            request.feet = standing.feet_position;
            request.eye = EyeOf(sender) ?? standing.feet_position;

            if (string.IsNullOrEmpty(request.requested_by))
            {
                request.requested_by = sender.SteamID.ToString();
            }
        }

        Begin(context, request);
    }

    [Command("solve", registerRaw: false, permission: "")]
    public void OnSolve(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        string argument = string.Join(" ", context.Args).Trim().Trim('"');

        if (argument.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            Reply(
                context,
                _solver.Cancel()
                    ? $" {ChatColors.Green}solve cancelled"
                    : $" {ChatColors.Red}nothing is solving"
            );
            return;
        }

        Vec3? target = AimPoint(player);

        if (target == null)
        {
            Reply(context, $" {ChatColors.Red}look at the spot you want it to land on");
            return;
        }

        ThrowSnapshot? standing = PracticeSystem.Where(player);
        Vec3? eye = EyeOf(player);

        if (standing == null || eye == null)
        {
            Reply(context, $" {ChatColors.Red}stand somewhere first");
            return;
        }

        string utility = UtilityInHand(player);

        var request = new SolveRequest
        {
            map = _library.Map,
            utility_type = utility,
            side = player.Controller.Team == Team.CT ? "CT" : "TERRORIST",
            name = argument.Length > 0 ? argument : $"solved {utility.ToLowerInvariant()}",
            target = target.Value,
            eye = eye.Value,
            feet = standing.feet_position,
            requested_by = player.SteamID.ToString(),
        };

        PracticeSolverUtility.Defaults(request);

        Reply(
            context,
            $" {ChatColors.Grey}solving a {utility.ToLowerInvariant()} onto {Point(target.Value)}..."
        );

        Begin(context, request);
    }

    // Nothing is emitted until the gate says the engine reproduces a seeded
    // throw on this map. The refusal is the feature: three hundred grenades
    // against a false premise produce lineups that look right and are not.
    private void Begin(ICommandContext context, SolveRequest request)
    {
        if (_solver.IsBusy)
        {
            Reply(context, $" {ChatColors.Red}{_solver.BusyWith} is already running");
            return;
        }

        if (string.IsNullOrEmpty(request.map))
        {
            request.map = _library.Map;
        }

        string map = request.map;
        CalibrationReport? cached = _solver.CalibrationFor(map);

        if (cached != null && !ShouldRetry(cached))
        {
            if (!cached.CanSolve())
            {
                Refuse(context, cached);
                return;
            }

            Launch(context, request, cached);
            return;
        }

        Reply(context, $" {ChatColors.Grey}calibrating the solver on {map} first...");

        bool started = _solver.Calibrate(
            map,
            CalibrationSamples(),
            report =>
            {
                if (!report.CanSolve())
                {
                    Refuse(context, report);
                    return;
                }

                Reply(context, $" {ChatColors.Green}calibrated: {report.message}");
                Launch(context, request, report);
            }
        );

        if (!started)
        {
            Reply(context, $" {ChatColors.Red}{_solver.BusyWith} is already running");
        }
    }

    private void Launch(ICommandContext context, SolveRequest request, CalibrationReport report)
    {
        ulong steamId = ulong.TryParse(request.requested_by, out ulong parsed) ? parsed : 0;

        bool started = _solver.Start(
            request,
            report,
            progress => Say(context, steamId, $" {ChatColors.Grey}{progress}"),
            (result, lineup) => Finished(context, request, steamId, result, lineup)
        );

        if (!started)
        {
            Reply(context, $" {ChatColors.Red}{_solver.BusyWith} is already running");
            return;
        }

        Reply(
            context,
            $" {ChatColors.Green}solving {ChatColors.Default}{request.utility_type} {ChatColors.Grey}(up to {request.max_grenades} grenades / {request.max_seconds:0}s, within {request.tolerance:0}u)"
        );
    }

    private void Finished(
        ICommandContext context,
        SolveRequest request,
        ulong steamId,
        SolveResult result,
        LineupRecord? lineup
    )
    {
        if (lineup == null)
        {
            Say(
                context,
                steamId,
                $" {ChatColors.Red}no throw found {ChatColors.Grey}({result.outcome}: {result.message})"
            );
            return;
        }

        lineup.plugin_version = ModuleVersion;

        Say(
            context,
            steamId,
            $" {ChatColors.Green}solved {ChatColors.Default}{lineup.name} {ChatColors.Grey}{result.message}"
        );
        Say(
            context,
            steamId,
            $" {ChatColors.Grey}stand at {Point(lineup.release.feet_position)}, look {lineup.release.yaw:0.0} / {lineup.release.pitch:0.0}, {lineup.strength?.ToLowerInvariant()} throw"
        );

        // A lineup with no author is one the panel has nowhere to file. The
        // throw is still worth reporting -- an operator solving over RCON to see
        // whether a spot is reachable does not always want it saved.
        if (steamId == 0)
        {
            Say(
                context,
                steamId,
                $" {ChatColors.Grey}not saved: pass steam=<id> to file it against an author"
            );
            return;
        }

        _library.Add(steamId, lineup);

        IPlayer? player = _system.Find(steamId);

        if (player != null && player.IsValid)
        {
            Apply(player, lineup);
        }

        // The same hand-off .save makes, so a solved lineup reaches the panel by
        // the one path that already retries.
        _ = Task.Run(async () =>
        {
            string? id = await _api.Ingest(lineup);

            Core.Scheduler.NextTick(() =>
            {
                if (id != null)
                {
                    lineup.id = id;
                    return;
                }

                Tell(
                    steamId,
                    $" {ChatColors.Red}{lineup.name} could not reach the panel; it will retry"
                );
            });
        });
    }

    private void Refuse(ICommandContext context, CalibrationReport report)
    {
        Reply(context, $" {ChatColors.Red}refusing to solve: {report.message}");
        Reply(context, $" {ChatColors.Grey}{Detail(report)}");
    }

    private void ReportCalibration(
        ICommandContext context,
        CalibrationReport report,
        bool cached
    )
    {
        string suffix = cached ? " (cached)" : "";

        Reply(
            context,
            report.CanSolve()
                ? $" {ChatColors.Green}{report.map}: ready{suffix} {ChatColors.Grey}{report.message}"
                : $" {ChatColors.Red}{report.map}: {report.status}{suffix} {ChatColors.Grey}{report.message}"
        );
        Reply(context, $" {ChatColors.Grey}{Detail(report)}");

        foreach (LaunchCheck check in report.launch_checks)
        {
            Reply(
                context,
                $" {ChatColors.Grey}  {check.strength} @ {check.pitch:0.0} pitch: {check.position_error:0.00}u, {check.direction_error:0.000} deg, {check.speed_ratio:0.000}x {(check.passed ? "ok" : "FAILED")}"
            );
        }
    }

    private static string Detail(CalibrationReport report)
    {
        string corrections =
            report.speed_corrections.Count == 0
                ? "none"
                : string.Join(
                    ", ",
                    report.speed_corrections.Select(pair => $"{pair.Key} {pair.Value:0.000}x")
                );

        string replay =
            report.seed_replay_error < 0f
                ? "not run"
                : $"{report.seed_replay_error:0.0}u";

        return $"{report.launch_checks.Count} samples, worst {report.WorstPositionError():0.00}u / {report.WorstDirectionError():0.000} deg, speed {corrections}, seed replay {replay}";
    }

    // Only throws this session can calibrate. A lineup from the panel carries a
    // seed but not the stance or the release strength behind it, so it can say
    // nothing about whether the launch model is right -- and a sample that
    // cannot fail is not a check.
    private List<LineupRecord> CalibrationSamples()
    {
        var samples = new List<LineupRecord>();

        foreach (ulong steamId in _system.ConnectedSteamIds())
        {
            samples.AddRange(_recorder.HistoryFor(steamId));
        }

        return samples;
    }

    // A missing sample is the one failure that fixes itself: somebody throws a
    // grenade and the answer changes. Everything else is a property of the
    // build and stays cached until the map or the plugin changes.
    private static bool ShouldRetry(CalibrationReport report)
    {
        return report.status == nameof(eCalibrationStatus.NoSample);
    }

    private Vec3? AimPoint(IPlayer player)
    {
        CCSPlayerPawn? pawn = player.PlayerPawn;
        Vec3? eye = EyeOf(player);

        if (pawn == null || !pawn.IsValid || eye == null)
        {
            return null;
        }

        var start = new Vector(eye.Value.x, eye.Value.y, eye.Value.z);
        QAngle angles = pawn.EyeAngles;

        // The trace starts inside the thrower's own bounding box, so the pawn
        // has to be excluded or the crosshair resolves to the player's chest.
        TraceParams options = TraceParams
            .Builder()
            .WithLineRay()
            .IgnoreEntities(new[] { (CEntityInstance)pawn })
            .Build();

        TraceResult trace = Core.Trace.TraceShapeAngle(in start, in angles, 8192f, options);

        if (!trace.DidHit)
        {
            return null;
        }

        Vector hit = trace.HitPoint;

        return new Vec3(hit.X, hit.Y, hit.Z);
    }

    private static Vec3? EyeOf(IPlayer player)
    {
        CCSPlayerPawn? pawn = player.PlayerPawn;
        Vector? origin = pawn?.AbsOrigin;

        if (pawn == null || !pawn.IsValid || origin == null)
        {
            return null;
        }

        return new Vec3(
            origin.Value.X,
            origin.Value.Y,
            origin.Value.Z + pawn.ViewOffset.Z.Value
        );
    }

    private static string UtilityInHand(IPlayer player)
    {
        CBasePlayerWeapon? active = player.PlayerPawn?.WeaponServices?.ActiveWeapon.Value;

        if (active == null || !active.IsValid)
        {
            return nameof(eUtilityType.Smoke);
        }

        return PracticeLineupUtility.UtilityTypeForWeapon(active.DesignerName ?? "")
            ?? nameof(eUtilityType.Smoke);
    }

    private static string Point(Vec3 point)
    {
        return $"{point.x:0} {point.y:0} {point.z:0}";
    }

    // A solve outlives the command that started it, and an RCON caller is long
    // gone by the time it lands. Whoever is still there hears about it.
    private void Say(ICommandContext context, ulong steamId, string message)
    {
        if (steamId != 0 && _system.Find(steamId) != null)
        {
            Tell(steamId, message);
            return;
        }

        Reply(context, message);
    }
}
