using FiveStack.Utilities;

namespace UtilityPractice;

// Choosing a lineup: the radar and the list in one panel, over one set of
// filters. A spot on the radar narrows the list; a row in the list is the
// choice.
public class NadePickTemplate : HudTemplate
{
    public record Row(string Id, string Label, string Value, bool Selected = false);

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

    private readonly List<Row> _rows = new();
    private readonly List<Marker> _markers = new();

    public override HudLayoutSlots Slots => HudSlots.Pick;

    public override bool CapturesInput => true;

    public string Title { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Page { get; set; } = "";
    public string Side { get; set; } = "all";
    public string Type { get; set; } = "all";
    public string Scope { get; set; } = "all";
    public bool HasPager { get; set; }
    public bool Here { get; set; }
    public bool Filtering { get; set; }

    // Null on a map with no radar to draw: the list has the panel to itself.
    public string? MapClass { get; set; }
    public string Focus { get; set; } = "";

    public NadePickTemplate Add(Row row)
    {
        if (_rows.Count >= HudSlots.ListRows)
        {
            throw new InvalidOperationException(
                $"{HudSlots.NadePick} holds {HudSlots.ListRows} rows, page before adding more"
            );
        }

        _rows.Add(row);

        return this;
    }

    public NadePickTemplate Add(Marker marker)
    {
        if (_markers.Count >= HudSlots.MapMarkers)
        {
            throw new InvalidOperationException(
                $"{HudSlots.NadePick} holds {HudSlots.MapMarkers} markers; cluster harder"
            );
        }

        _markers.Add(marker);

        return this;
    }

    public override void Apply(HudSurface surface)
    {
        surface.Set("title", Title);
        surface.Set("tag", Tag);
        surface.Set("page", Page);
        surface.Set("focus", Focus);

        for (int index = 0; index < HudSlots.ListRows; index++)
        {
            int slot = index + 1;
            Row? row = index < _rows.Count ? _rows[index] : null;

            // An empty row still has to be written: the slot may be holding the
            // previous page's text.
            surface.Set($"row{slot}", row?.Label ?? "");
            surface.Set($"row{slot}v", row?.Value ?? "");
            surface.Class($"row{slot}", "hidden", row == null);
            surface.Class($"row{slot}", "selected", row?.Selected == true);
        }

        foreach (string side in HudSlots.ListSides)
        {
            surface.Class($"side_{side}", "on", side == Side);
        }

        foreach (string scope in HudSlots.ListScopes)
        {
            surface.Class($"scope_{scope}", "on", scope == Scope);
        }

        foreach (string type in HudSlots.ListTypes)
        {
            surface.Class($"type_{type}", "on", type == Type);
        }

        surface.Class("here", "on", Here);
        surface.Class("reset", "hidden", !Filtering);
        surface.Class("pager", "hidden", !HasPager);
        surface.Class("mapcol", "hidden", MapClass == null);

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
            Marker? marker = MapClass != null && index < _markers.Count ? _markers[index] : null;

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
            buttonId.StartsWith("row", StringComparison.Ordinal)
            && int.TryParse(buttonId.AsSpan(3), out int row)
            && row >= 1
            && row <= _rows.Count
        )
        {
            return _rows[row - 1].Id;
        }

        if (
            buttonId.StartsWith("m", StringComparison.Ordinal)
            && int.TryParse(buttonId.AsSpan(1), out int marker)
            && marker >= 1
            && marker <= _markers.Count
        )
        {
            return $"target:{_markers[marker - 1].Id}";
        }

        return buttonId;
    }
}
