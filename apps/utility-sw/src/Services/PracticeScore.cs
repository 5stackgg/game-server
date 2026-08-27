using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using static SwiftlyS2.Shared.Helper;

namespace UtilityPractice;

// Scores a throw against the lineup the thrower had loaded.
//
// The panel recomputes the distance from the lineup it owns and treats what is
// reported here as advisory, so this reports and does not argue: the success
// flag is only filled in once the panel has told us what radius it is using,
// and the streak a player is shown is always the panel's answer.
public class PracticeScore
{
    private readonly ISwiftlyCore _core;
    private readonly UtilityConfig _config;
    private readonly UtilityApiClient _api;
    private readonly PracticeSession _session;
    private readonly PracticeSystem _system;

    // The last radius the panel used, so the advisory flag is a belief we were
    // given rather than a number hard-coded here.
    private float? _radius;

    private readonly HashSet<string> _mastered = new HashSet<string>();

    public PracticeScore(
        ISwiftlyCore core,
        UtilityConfig config,
        UtilityApiClient api,
        PracticeSession session,
        PracticeSystem system
    )
    {
        _core = core;
        _config = config;
        _api = api;
        _session = session;
        _system = system;
    }

    // Raised once a throw has been through the panel, or once it is known that
    // it could not be. A null result is "not scored", which is not the same as
    // a miss: a drill counts on being able to tell the two apart.
    public event Action<ulong, string, UtilityPracticeResult?>? Scored;

    public void Reset()
    {
        _mastered.Clear();
    }

    // Raised by the recorder once a throw is over, which is the only moment
    // both the thrower and the landing point are known.
    public void OnFinalized(LineupRecord thrown)
    {
        if (!_config.IsConnected() || !ulong.TryParse(thrown.author_steam_id, out ulong steamId))
        {
            return;
        }

        LineupRecord? loaded = _system.StateFor(steamId).Loaded;

        // Nothing loaded is not a practice attempt, and a lineup that only
        // exists on this server has no id for the panel to score against.
        if (loaded == null || string.IsNullOrEmpty(loaded.id))
        {
            return;
        }

        // Throwing a flash while a smoke lineup is loaded is a different throw,
        // not a missed one.
        if (loaded.utility_type != thrown.utility_type)
        {
            return;
        }

        Vec3 landing = thrown.detonation_position;
        float distance = (landing - loaded.detonation_position).Length();

        string name = string.IsNullOrEmpty(loaded.name) ? "that lineup" : loaded.name;

        // A scratch throw -- a meta spot, or a draft being tested from the
        // panel before it is saved -- has no row behind it, and the panel
        // rejects a result whose lineup id is not a uuid. Asking anyway got a
        // 400 back and told the player "the panel did not answer", which reads
        // as a broken panel when in fact everything needed to judge the throw
        // is already here. So judge it here. There is nothing to persist it
        // against, which is why it carries no streak and no tally.
        if (!PracticeLineupUtility.IsPanelId(loaded.id))
        {
            ScoreLocally(steamId, loaded.client_id, name, distance);
            return;
        }

        var payload = UtilityPracticeResultPayload.For(
            _config.ServerId,
            _session.Current?.id ?? Guid.Empty,
            loaded.id,
            steamId,
            landing,
            _radius == null ? null : distance <= _radius
        );

        string lineupId = loaded.id;
        string key = $"{lineupId}:{steamId}";

        _ = Task.Run(async () =>
        {
            UtilityPracticeResult? result = await _api.PracticeResult(payload);

            _core.Scheduler.NextTick(() => Report(steamId, lineupId, key, name, result, distance));
        });
    }

    // Judged here rather than by the panel, for a throw the panel has no row
    // for. The radius is still the panel's whenever it has said one this
    // session; the fallback only stands in before it ever has.
    private void ScoreLocally(ulong steamId, string lineupId, string name, float distance)
    {
        float radius = _radius ?? PracticeLineupUtility.FallbackSuccessRadius;
        bool success = distance <= radius;

        IPlayer? player = _system.Find(steamId);

        if (player != null && player.IsValid)
        {
            player.SendChat(
                (
                    success
                        ? $" {ChatColors.Green}hit {ChatColors.Default}{name} {ChatColors.Grey}{distance:0}u - not saved, so it is not counted"
                        : $" {ChatColors.Red}miss {ChatColors.Default}{name} {ChatColors.Grey}{distance:0}u, needs {radius:0}u"
                ).Colored()
            );
        }

        // A drill counts on being able to tell a miss from an unanswered throw,
        // and this is an answer -- so it carries one, with the tallies left at
        // zero because there is nothing behind them.
        Scored?.Invoke(
            steamId,
            lineupId,
            new UtilityPracticeResult
            {
                success = success,
                distance = distance,
                radius = radius,
            }
        );
    }

    private void Report(
        ulong steamId,
        string lineupId,
        string key,
        string name,
        UtilityPracticeResult? result,
        float measured
    )
    {
        if (result != null)
        {
            _radius = result.radius;
        }

        Announce(steamId, key, name, result, measured);

        // Raised last and unconditionally: the verdict belongs on the player's
        // screen before whatever a run says about it, and a run's bookkeeping
        // is not allowed to depend on the thrower still standing there.
        Scored?.Invoke(steamId, lineupId, result);
    }

    private void Announce(
        ulong steamId,
        string key,
        string name,
        UtilityPracticeResult? result,
        float measured
    )
    {
        IPlayer? player = _system.Find(steamId);

        if (player == null || !player.IsValid)
        {
            return;
        }

        if (result == null)
        {
            player.SendChat(
                $" {ChatColors.Grey}{measured:0}u from {name} {ChatColors.Default}(not scored; the panel did not answer)".Colored()
            );
            return;
        }

        player.SendChat(
            (
                result.success
                    ? $" {ChatColors.Green}hit {ChatColors.Default}{name} {ChatColors.Grey}{result.distance:0}u - streak {result.current_streak} (best {result.best_streak})"
                    : $" {ChatColors.Red}miss {ChatColors.Default}{name} {ChatColors.Grey}{result.distance:0}u, needs {result.radius:0}u - {result.successes}/{result.attempts}"
            ).Colored()
        );

        if (result.mastered_at == null || !_mastered.Add(key))
        {
            return;
        }

        player.SendChat(
            $" {ChatColors.Gold}mastered {ChatColors.Default}{name} {ChatColors.Grey}({result.best_streak} in a row)".Colored()
        );
        player.SendCenter($"mastered\n{name}");
    }
}
