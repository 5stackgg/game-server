using FiveStack.Utilities;

namespace UtilityPractice;

public class NadeMapTemplate : HudTemplate
{
    public record Marker(
        string Id,
        string Name,
        int Column,
        int Row,
        string Type,
        int Count,
        bool Reachable = false,
        bool Loaded = false,
        bool Selected = false
    );

    private readonly List<Marker> _markers = new();

    public override HudLayoutSlots Slots => HudSlots.Map;

    public override bool CapturesInput => true;

    public required string MapClass { get; init; }
    public string Title { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Focus { get; set; } = "";

    // Null until a marker is clicked. The map answers in place rather than
    // handing the player off to the list.
    public Detail? Selected { get; set; }

    public record Detail(string Name, string Meta, string Load, string? List);

    public NadeMapTemplate Add(Marker marker)
    {
        if (_markers.Count >= HudSlots.MapMarkers)
        {
            throw new InvalidOperationException(
                $"nade_map holds {HudSlots.MapMarkers} markers; cluster harder"
            );
        }

        _markers.Add(marker);

        return this;
    }

    public override void Apply(HudSurface surface)
    {
        surface.Set("title", Title);
        surface.Set("tag", Tag);
        surface.Set("focus", Focus);
        surface.Set("dname", Selected?.Name ?? "");
        surface.Set("dmeta", Selected?.Meta ?? "");
        surface.Set("dload", Selected?.Load ?? "");
        surface.Set("dlist", Selected?.List ?? "");
        surface.Class("detail", HudSlots.Shown, Selected != null);
        surface.Class("list", "hidden", Selected?.List == null);

        // One radar image per map, picked out of the class group rather than
        // bound through a dialog variable -- image binding by variable is not a
        // thing we could verify, and a class swap is.
        foreach (string map in RadarMaps.All)
        {
            surface.Class("radar", map, map == MapClass);
        }

        for (int index = 0; index < HudSlots.MapMarkers; index++)
        {
            int slot = index + 1;
            Marker? marker = index < _markers.Count ? _markers[index] : null;

            surface.Set($"c{slot}", marker is { Count: > 1 } ? marker.Count.ToString() : "");
            surface.Set($"n{slot}", marker?.Name ?? "");
            surface.Class($"m{slot}", "hidden", marker == null);

            if (marker == null)
            {
                continue;
            }

            surface.Pick($"m{slot}", "x", marker.Column, HudSlots.MapGrid);
            surface.Pick($"m{slot}", "y", marker.Row, HudSlots.MapGrid);

            foreach (string type in RadarMaps.Types)
            {
                surface.Class($"m{slot}", type, type == marker.Type);
            }

            surface.Class($"m{slot}", "reachable", marker.Reachable);
            surface.Class($"m{slot}", "loaded", marker.Loaded);
            surface.Class($"m{slot}", "selected", marker.Selected);
        }
    }

    public override string Resolve(string buttonId)
    {
        if (
            buttonId.StartsWith("m", StringComparison.Ordinal)
            && int.TryParse(buttonId.AsSpan(1), out int slot)
            && slot >= 1
            && slot <= _markers.Count
        )
        {
            return $"target:{_markers[slot - 1].Id}";
        }

        return buttonId;
    }
}
