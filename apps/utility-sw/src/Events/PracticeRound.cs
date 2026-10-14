using System.Runtime.InteropServices;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Memory;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using static SwiftlyS2.Shared.Helper;

namespace UtilityPractice;

// The argument order is the Linux build's, which is the only one a practice
// server runs: SwiftlyS2 calls the same function with the delay second on
// Windows.
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void TerminateRoundDelegate(nint rules, uint reason, nint team, float delay);

// The first player on an empty team -- a bot put on the other side, or a
// friend joining it -- ends the round as "Game Commencing" and restarts it
// three seconds later: everybody back to spawn, every smoke gone. Round win
// conditions are already off here, so that is the one ending left, and it is
// refused.
public partial class UtilityPracticePlugin
{
    private const string TerminateRoundSignature = "CGameRules::TerminateRound";

    private IUnmanagedFunction<TerminateRoundDelegate>? _terminateRoundFunc;
    private Guid _terminateRoundHookId;
    private bool _commencingRefused;

    private void InitializeTerminateRoundHook()
    {
        if (_terminateRoundFunc != null || !OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            if (!Core.GameData.TryGetSignature(TerminateRoundSignature, out nint address))
            {
                _logger.LogWarning("Failed to find TerminateRound signature");
                return;
            }

            _terminateRoundFunc = Core.Memory.GetUnmanagedFunctionByAddress<TerminateRoundDelegate>(
                address
            );

            _terminateRoundHookId = _terminateRoundFunc.AddHook(next =>
                (rules, reason, team, delay) =>
                {
                    if (reason == (uint)RoundEndReason.GameCommencing)
                    {
                        if (!_commencingRefused)
                        {
                            _commencingRefused = true;
                            _logger.LogInformation(
                                "a team filled; kept the round going instead of restarting it"
                            );
                        }

                        return;
                    }

                    next()(rules, reason, team, delay);
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize TerminateRound hook");
        }
    }

    private void UninstallTerminateRoundHook()
    {
        try
        {
            if (_terminateRoundFunc != null && _terminateRoundHookId != Guid.Empty)
            {
                _terminateRoundFunc.RemoveHook(_terminateRoundHookId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove TerminateRound hook");
        }

        _terminateRoundFunc = null;
        _terminateRoundHookId = Guid.Empty;
    }

    // A round that runs out of clock with nothing allowed to end it carries
    // on at 0:00, and from then on the game's animation crawls until the
    // level is loaded again. So it is loaded again, the same map, with a
    // minute's warning. Not under a render or a discovery: those change
    // level themselves, and reloading would drop the batch they are on.
    private const int ClockWarnSeconds = 60;
    private static readonly TimeSpan ClockReloadWait = TimeSpan.FromSeconds(30);

    private bool _clockWarned;
    private DateTime _clockReloaded = DateTime.MinValue;

    private void ReloadWhenTheClockRunsOut()
    {
        if (_config.RenderMode || _discovery.IsBusy)
        {
            return;
        }

        if (DateTime.UtcNow - _clockReloaded < ClockReloadWait)
        {
            return;
        }

        CCSGameRules? rules = Core.EntitySystem.GetGameRules();

        if (rules == null || rules.WarmupPeriod || rules.FreezePeriod)
        {
            return;
        }

        float left =
            rules.RoundStartTime.Value + rules.RoundTime - Core.Engine.GlobalVars.CurrentTime;

        if (left > ClockWarnSeconds)
        {
            _clockWarned = false;
            return;
        }

        if (!_clockWarned)
        {
            _clockWarned = true;
            AnnounceClockReload((int)MathF.Ceiling(left));
            return;
        }

        if (left > 0f)
        {
            return;
        }

        string workshop = Core.Engine.WorkshopId ?? "";
        string map = PracticeMapChangeUtility.IsWorkshopId(workshop)
            ? workshop
            : Core.Engine.GlobalVars.MapName.ToString();

        _clockReloaded = DateTime.UtcNow;
        _logger.LogInformation("the round clock ran out; reloading {map}", map);
        Core.Engine.ExecuteCommand(PracticeMapChangeUtility.Command(map));
    }

    private void AnnounceClockReload(int seconds)
    {
        string chat =
            $" {ChatColors.Green}the round clock is nearly out {ChatColors.Grey}-- "
            + $"the map reloads in {ChatColors.Default}{Math.Max(seconds, 1)}s";

        foreach (IPlayer player in Core.PlayerManager.GetAllPlayers())
        {
            if (player == null || !player.IsValid || player.IsFakeClient)
            {
                continue;
            }

            player.SendChat(chat.Colored());
        }
    }
}
