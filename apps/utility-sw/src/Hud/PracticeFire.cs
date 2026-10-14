using FiveStack.Utilities;
using SwiftlyS2.Shared.Players;

namespace UtilityPractice;

// Who a fire reached, and not how much it did: a bot stands in a molotov
// until the flames go out, where a player would walk out of it, so the damage
// says how long a bot stood still. Whether the fire got to them is what a
// molotov lineup is for.
public partial class UtilityPracticePlugin
{
    private class FireReport
    {
        public required string Title;
        public required int Started;
        public int Last;
        public readonly Dictionary<string, PracticeCardGroups.Burn> Burned = new();
    }

    private readonly Dictionary<ulong, FireReport> _fires = new();

    // A fire burns for about seven seconds, and everything it hurts in that
    // time is the one fire.
    private const int FireSameTicks = 8 * 64;

    private const int CounterTerrorists = 3;

    private void RecordBurn(IPlayer thrower, IPlayer burned)
    {
        ulong steamId = thrower.SteamID;

        if (!_fires.TryGetValue(steamId, out FireReport? report) || _aimTick - report.Started > FireSameTicks)
        {
            report = new FireReport
            {
                Title = thrower.PlayerPawn?.TeamNum == CounterTerrorists ? "Incendiary" : "Molotov",
                Started = _aimTick,
            };

            _fires[steamId] = report;
        }

        report.Last = _aimTick;

        bool self = burned.SteamID == steamId && !burned.IsFakeClient;

        // A bot is a target whichever side it was put on, as with a flash.
        bool mate =
            !burned.IsFakeClient
            && !self
            && burned.Controller.Team == thrower.Controller.Team;

        report.Burned[self ? "You" : burned.Controller.PlayerName] = new PracticeCardGroups.Burn(
            !self && !mate,
            self || mate
        );
    }

    private NadeBarTemplate.FlashCard? FireFor(ulong steamId)
    {
        if (!_fires.TryGetValue(steamId, out FireReport? report))
        {
            return null;
        }

        if (_aimTick - report.Last > FlashCardTicks || _flashCardOff.Contains(steamId))
        {
            _fires.Remove(steamId);

            return null;
        }

        var rows = new List<NadeBarTemplate.FlashRow>();
        List<PracticeCardGroups.Burn> burns = report.Burned.Values.ToList();

        if (report.Burned.Count > HudSlots.FlashRows)
        {
            foreach (PracticeCardGroups.Group group in PracticeCardGroups.Fire(burns))
            {
                rows.Add(new NadeBarTemplate.FlashRow($"{group.Count} {group.Label}", "", "", 0, 0, group.Bad));
            }
        }
        else
        {
            foreach ((string who, PracticeCardGroups.Burn burn) in report.Burned.OrderBy(entry => entry.Value.Bad))
            {
                rows.Add(new NadeBarTemplate.FlashRow(who, "", burn.Bad ? "your side" : "burned", 0, 0, burn.Bad));
            }
        }

        int targets = burns.Count(burn => burn.Target);
        string foot = targets switch
        {
            0 => "No targets burned",
            1 => "1 target burned",
            _ => $"{targets} targets burned",
        };

        if (burns.Any(burn => burn.Bad))
        {
            foot += " · burned your own side";
        }

        return new NadeBarTemplate.FlashCard(report.Title, "took fire damage", foot, rows, Fire: true);
    }

    // One card on the bar: whichever of the flash and the fire went off last.
    private NadeBarTemplate.FlashCard? CardFor(ulong steamId)
    {
        NadeBarTemplate.FlashCard? flash = FlashFor(steamId);
        NadeBarTemplate.FlashCard? fire = FireFor(steamId);

        if (flash == null || fire == null)
        {
            return flash ?? fire;
        }

        int flashed = _flashes.TryGetValue(steamId, out FlashReport? popped) ? popped.Tick : -1;
        int burned = _fires.TryGetValue(steamId, out FireReport? lit) ? lit.Started : -1;

        return burned >= flashed ? fire : flash;
    }
}
