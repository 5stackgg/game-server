using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using SwiftlyS2.Shared.Players;

namespace UtilityPractice;

// Changing map from the menu. The website does the change, exactly as its own
// button does, so the session it keeps, the host-only rule and the countdown
// are all the same ones: this only asks.
public partial class UtilityPracticePlugin
{
    private class MapChoices
    {
        public required int Asked;
        public PracticeMaps? Maps;
    }

    private readonly Dictionary<ulong, MapChoices> _mapChoices = new();

    private const int MapChoicesFreshTicks = 20 * 64;

    // "de_dust2" reads as "Dust2" on a wedge.
    private static string MapLabel(string map)
    {
        int prefix = map.IndexOf('_');
        string name = prefix > 0 && prefix < map.Length - 1 ? map[(prefix + 1)..] : map;

        return PracticeLineupUtility.TitleCase(name.Replace('_', ' '));
    }

    // Null while the panel has not answered yet.
    private PracticeMaps? MapChoicesFor(IPlayer player)
    {
        ulong steamId = player.SteamID;

        if (
            _mapChoices.TryGetValue(steamId, out MapChoices? choices)
            && _aimTick - choices.Asked < MapChoicesFreshTicks
        )
        {
            return choices.Maps;
        }

        var asking = new MapChoices { Asked = _aimTick, Maps = choices?.Maps };

        _mapChoices[steamId] = asking;

        _ = Task.Run(async () =>
        {
            PracticeMaps maps = await _api.MapsFor(steamId) ?? new PracticeMaps();

            Core.Scheduler.NextTick(() =>
            {
                asking.Maps = maps;

                IPlayer? still = _system.Find(steamId);

                if (
                    still != null
                    && still.IsValid
                    && _hud != null
                    && _hud.IsOpen(still, HudSlots.Wheel)
                    && WheelPath(steamId).LastOrDefault() == "maps"
                )
                {
                    RenderWheel(still);
                }
            });
        });

        return asking.Maps;
    }

    // A wheel of maps, and a last wedge onto the next wheel of them when they
    // do not fit. The page is how many map wheels deep the player is, so Back
    // walks back through them.
    private WheelPage MapsPage(IPlayer player)
    {
        PracticeMaps? choices = MapChoicesFor(player);

        if (choices == null)
        {
            return new WheelPage(
                "Change Map",
                new WheelItem?[] { new WheelItem("none", "Loading", "asking the website", Disabled: true) }
            );
        }

        if (choices.maps.Count == 0)
        {
            return new WheelPage(
                "Change Map",
                new WheelItem?[] { new WheelItem("none", "No Maps", "the website offered none", Disabled: true) }
            );
        }

        bool paged = choices.maps.Count > HudSlots.WheelSlots;
        int per = paged ? HudSlots.WheelSlots - 1 : HudSlots.WheelSlots;
        int pages = (choices.maps.Count + per - 1) / per;
        int page = (WheelPath(player.SteamID).Count(step => step == "maps") - 1) % pages;

        var items = choices
            .maps.Skip(page * per)
            .Take(per)
            .Select(map =>
            {
                bool here = map == choices.map_name;

                return (WheelItem?)
                    new WheelItem(
                        $"map:{map}",
                        MapLabel(map),
                        here ? "you are here"
                            : choices.can_change ? ""
                            : "host only",
                        Picked: here,
                        Disabled: here || !choices.can_change
                    );
            })
            .ToList();

        if (paged)
        {
            items.Add(new WheelItem("maps", "More Maps", $"{page + 1} / {pages}", Opens: "maps"));
        }

        return new WheelPage("Change Map", items);
    }

    private void ChangeMapTo(IPlayer player, string map)
    {
        ulong steamId = player.SteamID;

        Toast(player, $"Asking for {MapLabel(map)}");

        _ = Task.Run(async () =>
        {
            string? refused = await _api.ChangePracticeMap(steamId, map);

            Core.Scheduler.NextTick(() =>
            {
                IPlayer? still = _system.Find(steamId);

                if (still == null || !still.IsValid)
                {
                    return;
                }

                if (refused != null && refused.Length > 0)
                {
                    Toast(still, char.ToUpperInvariant(refused[0]) + refused[1..]);
                }
            });
        });
    }
}
