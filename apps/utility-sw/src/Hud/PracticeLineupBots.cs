using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace UtilityPractice;

public partial class UtilityPracticePlugin
{
    private static string BotCount(int bots)
    {
        return bots == 1 ? "1 bot" : $"{bots} bots";
    }

    // Only lineups with bots are looked through, and nothing is loaded: a drill
    // or a loaded throw holds the panels still, and must not stop the player
    // pointing at the lineup they mean here.
    private LineupBots.Choice BotChoice(IPlayer player, CCSPlayerPawn pawn, Vec3 at)
    {
        List<LineupRecord> withBots = Shown(player.SteamID)
            .Where(lineup => lineup.bots.Count > 0)
            .ToList();

        if (withBots.Count == 0)
        {
            return default;
        }

        List<LineupRecord> here = PracticeReplay.SpotAt(withBots, at);

        return LineupBots.Pick(LookingAt(pawn, at, withBots, here, null), here);
    }

    private NadeBarTemplate.Key BotKey(IPlayer player, CCSPlayerPawn pawn, Vec3 at)
    {
        LineupBots.Choice choice = BotChoice(player, pawn, at);

        if (choice.Waiting > 1)
        {
            return new NadeBarTemplate.Key($"Point at one · {choice.Waiting} here have bots");
        }

        if (choice.Lineup == null)
        {
            return new NadeBarTemplate.Key("Spawn a lineup's bots", Dim: true);
        }

        int missing = LineupBots.Missing(choice.Lineup.bots, _bots).Count;

        return missing == 0
            ? new NadeBarTemplate.Key("Its bots are up", On: true)
            : new NadeBarTemplate.Key(
                $"Spawn {BotCount(missing)} · {Clip(LineupName(choice.Lineup), LegendNameLength)}"
            );
    }

    private static string LineupName(LineupRecord lineup)
    {
        return PracticeLineupUtility.TitleCase(DrillUtility.Name(lineup));
    }

    private void SpawnLineupBots(IPlayer player)
    {
        CCSPlayerPawn? pawn = player.PlayerPawn;

        if (pawn == null || !pawn.IsValid)
        {
            return;
        }

        Vector origin = pawn.AbsOrigin ?? new Vector(0, 0, 0);
        LineupBots.Choice choice = BotChoice(player, pawn, new Vec3(origin.X, origin.Y, origin.Z));

        if (choice.Lineup == null)
        {
            Toast(player, choice.Waiting > 1 ? "Point at the lineup you want" : "No lineup with bots here");

            return;
        }

        List<LineupBot> missing = LineupBots.Missing(choice.Lineup.bots, _bots);

        if (missing.Count == 0)
        {
            Toast(player, "Those bots are already up");

            return;
        }

        int room = BotSlotsLeft();

        if (room == 0)
        {
            Toast(player, _bots.Count >= MaxBots ? $"{MaxBots} bots at most" : "The server has no room for another bot");

            return;
        }

        List<ThrowSnapshot> spots = missing
            .Take(room)
            .Select(bot => new ThrowSnapshot { feet_position = new Vec3(bot.x, bot.y, bot.z), yaw = bot.yaw })
            .ToList();

        AskForBots(player, pawn, spots);

        Toast(
            player,
            spots.Count == missing.Count
                ? $"{BotCount(spots.Count)} · {LineupName(choice.Lineup)}"
                : $"{spots.Count} of {BotCount(missing.Count)} · no room for the rest"
        );
    }

    // What the player is on or pointing at: a lineup loaded and walked away
    // from is not one they are looking at to change.
    private LineupRecord? KeepTarget(IPlayer player)
    {
        CCSPlayerPawn? pawn = player.PlayerPawn;

        if (pawn == null || !pawn.IsValid)
        {
            return null;
        }

        (LineupRecord? lineup, _, _) = Focused(player, pawn);

        return
            lineup != null
            && PracticeLineupUtility.IsPanelId(lineup.id)
            && lineup.author_steam_id == player.SteamID.ToString()
            ? lineup
            : null;
    }

    private void KeepBotsOnLineup(IPlayer player)
    {
        LineupRecord? lineup = KeepTarget(player);

        if (lineup == null)
        {
            Toast(player, "Point at a lineup of yours");

            return;
        }

        if (_bots.Count == 0)
        {
            Toast(player, "Place a bot first");

            return;
        }

        ulong steamId = player.SteamID;
        string id = lineup.id!;
        List<LineupBot> bots = LineupBots.Snapshot(_bots, MaxBots);

        _ = Task.Run(async () =>
        {
            UtilityApiClient.EditResult result = await _api.UpdateBots(id, steamId, bots);

            Core.Scheduler.NextTick(() =>
            {
                bool saved = result.Outcome == UtilityApiClient.eEditOutcome.Saved;

                if (saved)
                {
                    lineup.bots = bots;
                }

                IPlayer? asker = _system.Find(steamId);

                if (asker == null || !asker.IsValid)
                {
                    return;
                }

                Toast(
                    asker,
                    saved
                        ? $"{BotCount(bots.Count)} saved to {Clip(LineupName(lineup), LegendNameLength)}"
                        : "The panel did not save that"
                );
            });
        });
    }
}
