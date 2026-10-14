using FiveStack.Utilities;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace UtilityPractice;

// What a flash did: everybody it caught, for how long, and how much of that
// was seeing nothing at all. One number in chat said a flash had landed; it
// did not say on whom, or whether "2.1 seconds" meant blind or merely dazzled.
public partial class UtilityPracticePlugin
{
    private class FlashHit
    {
        public required string Who;
        public required float Seconds;
        public required float MaxAlpha;
        public required bool Target;
        public required bool Bad;
    }

    private class FlashReport
    {
        public required int Tick;
        public float? Fuse;
        public readonly List<FlashHit> Hits = new();
    }

    private readonly Dictionary<ulong, FlashReport> _flashes = new();
    private readonly HashSet<ulong> _flashCardOff = new();

    // One flash blinds everybody on the same tick. A second flash this soon
    // after the first is the same pop being reported for the next player.
    private const int FlashSameTicks = 4;

    private const int FlashCardTicks = 10 * 64;

    // The last three seconds of any blindness are the fade back to sight, and
    // everything before them is white. That is the client's own rule for
    // drawing the effect, so it is the honest split to show.
    private const float FlashFadeSeconds = 3f;

    // The bars are drawn against this: about as long as a flash ever lasts.
    private const float FlashBarSeconds = 5f;

    // How bright it got, out of 255, says which way they were facing.
    private const float FlashFacingAlpha = 250f;
    private const float FlashSideAlpha = 150f;

    private bool FlashCardFor(ulong steamId)
    {
        return _hud != null && UseHud(steamId) && !_flashCardOff.Contains(steamId);
    }

    private void RecordFlash(IPlayer thrower, IPlayer blinded, CCSPlayerPawn? pawn, float seconds)
    {
        ulong steamId = thrower.SteamID;

        if (
            !_flashes.TryGetValue(steamId, out FlashReport? report)
            || _aimTick - report.Tick > FlashSameTicks
        )
        {
            report = new FlashReport { Tick = _aimTick };

            if (
                _flying.TryGetValue(steamId, out Flight? flight)
                && flight.Designer == "flashbang_projectile"
            )
            {
                report.Fuse = (_aimTick - flight.Since) / 64f;
            }

            _flashes[steamId] = report;
        }

        bool self = blinded.SteamID == steamId && !blinded.IsFakeClient;
        float alpha = FlashFacingAlpha;

        try
        {
            if (pawn != null && pawn.IsValid)
            {
                alpha = pawn.FlashMaxAlpha;
            }
        }
        catch
        {
            alpha = FlashFacingAlpha;
        }

        // A bot is a target whichever side it was put on: it stands where an
        // enemy would. A person on the thrower's side is not.
        bool mate =
            !blinded.IsFakeClient
            && !self
            && blinded.Controller.Team == thrower.Controller.Team;

        report.Hits.Add(
            new FlashHit
            {
                Who = self ? "You" : blinded.Controller.PlayerName,
                Seconds = seconds,
                MaxAlpha = alpha,
                Target = !self && !mate,
                Bad = self || mate,
            }
        );
    }

    private NadeBarTemplate.FlashCard? FlashFor(ulong steamId)
    {
        if (!_flashes.TryGetValue(steamId, out FlashReport? report))
        {
            return null;
        }

        if (_aimTick - report.Tick > FlashCardTicks || _flashCardOff.Contains(steamId))
        {
            _flashes.Remove(steamId);

            return null;
        }

        int steps = HudSlots.FlashBarSteps - 1;
        var rows = new List<NadeBarTemplate.FlashRow>();

        if (report.Hits.Count > HudSlots.FlashRows)
        {
            List<PracticeCardGroups.FlashHit> hits = report
                .Hits.Select(hit => new PracticeCardGroups.FlashHit(
                    hit.Seconds,
                    MathF.Max(0f, hit.Seconds - FlashFadeSeconds),
                    hit.Target,
                    hit.Bad
                ))
                .ToList();

            foreach (PracticeCardGroups.Group group in PracticeCardGroups.Flash(hits))
            {
                rows.Add(
                    new NadeBarTemplate.FlashRow(
                        $"{group.Count} {group.Label}",
                        $"{group.Seconds:0.0}s",
                        "longest",
                        Bar(group.Seconds, steps),
                        Bar(group.White, steps),
                        group.Bad
                    )
                );
            }
        }

        foreach (
            FlashHit hit in rows.Count > 0
                ? Enumerable.Empty<FlashHit>()
                : report.Hits.OrderByDescending(hit => hit.Seconds).Take(HudSlots.FlashRows)
        )
        {
            float white = MathF.Max(0f, hit.Seconds - FlashFadeSeconds);

            rows.Add(
                new NadeBarTemplate.FlashRow(
                    hit.Who,
                    $"{hit.Seconds:0.0}s",
                    hit.Bad ? "your side"
                        : hit.MaxAlpha >= FlashFacingAlpha ? "facing it"
                        : hit.MaxAlpha >= FlashSideAlpha ? "side on"
                        : "turned away",
                    Bar(hit.Seconds, steps),
                    Bar(white, steps),
                    hit.Bad
                )
            );
        }

        int targets = report.Hits.Count(hit => hit.Target);
        int worked = report.Hits.Count(hit =>
            hit.Target && hit.Seconds - FlashFadeSeconds >= PracticeCardGroups.WorkedWhiteSeconds
        );
        string foot = targets == 0 ? "No targets caught" : $"{worked} of {targets} fully blind 1s+";

        if (report.Hits.Any(hit => hit.Bad))
        {
            foot += " · caught your own side";
        }

        return new NadeBarTemplate.FlashCard(
            "Flash",
            report.Fuse is float fuse ? $"popped {fuse:0.0}s after release" : "",
            foot,
            rows
        );
    }

    private static int Bar(float seconds, int steps)
    {
        return Math.Clamp((int)MathF.Round(seconds / FlashBarSeconds * steps), 0, steps);
    }
}
