using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities.Practice;
using FiveStack.Utilities;

namespace NadePractice;

// The solver is a Swiftly-only feature, and these exist so it fails as an
// answer rather than as silence.
//
// It works by firing candidate grenades and reading where they actually land,
// which needs a way to put a projectile into the world from a chosen position
// and velocity. Swiftly exposes that; CounterStrikeSharp does not. The gap is
// not something this plugin can paper over: the alternative is reimplementing
// CS2's grenade physics against the collision mesh, which is the thing the
// whole design exists to avoid.
public partial class NadePracticePlugin
{
    private const string Unsupported =
        "the solver needs a grenade emit API, which CounterStrikeSharp does not expose; run the SwiftlyS2 build of this plugin to use it";

    [ConsoleCommand("css_solve", "Solves a throw onto the spot you are looking at")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnSolve(CCSPlayerController? player, CommandInfo command)
    {
        command.ReplyToCommand($" {ChatColors.Red}{Unsupported}");
    }

    [ConsoleCommand("nade_solver_solve", "Solves a throw onto a point (SwiftlyS2 only)")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnSolverSolve(CCSPlayerController? player, CommandInfo command)
    {
        command.ReplyToCommand($" {ChatColors.Red}{Unsupported}");
    }

    // Answers with the same report shape Swiftly would, so whatever is driving
    // this over RCON reads a refusal it understands rather than a missing
    // command.
    [ConsoleCommand("nade_solver_calibrate", "Checks the solver's premise (SwiftlyS2 only)")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnSolverCalibrate(CCSPlayerController? player, CommandInfo command)
    {
        CalibrationReport report = PracticeCalibrationUtility.Unsupported(
            _library.Map,
            Unsupported
        );

        command.ReplyToCommand(
            $" {ChatColors.Red}{report.map}: {report.status} {ChatColors.Grey}{report.message}"
        );
    }
}
