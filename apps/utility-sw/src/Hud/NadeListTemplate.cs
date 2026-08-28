using FiveStack.Utilities;

namespace UtilityPractice;

public class NadeListTemplate : HudTemplate
{
    public record Row(string Id, string Label, string Value, bool Selected = false, bool Disabled = false);

    private readonly List<Row> _rows = new();

    public override HudLayoutSlots Slots => HudSlots.List;

    public override bool CapturesInput => true;

    public string Title { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Page { get; set; } = "";
    public string Side { get; set; } = "all";
    public string Type { get; set; } = "all";
    public string Scope { get; set; } = "all";
    public bool HasPager { get; set; }

    public NadeListTemplate Add(Row row)
    {
        if (_rows.Count >= HudSlots.ListRows)
        {
            throw new InvalidOperationException(
                $"nade_list holds {HudSlots.ListRows} rows, page before adding more"
            );
        }

        _rows.Add(row);

        return this;
    }


    public override void Apply(HudSurface surface)
    {
        surface.Set("title", Title);
        surface.Set("tag", Tag);
        surface.Set("page", Page);

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
            surface.Class($"row{slot}", "disabled", row?.Disabled == true);
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

        surface.Class("pager", "hidden", !HasPager);
    }

    public override string Resolve(string buttonId)
    {
        if (
            buttonId.StartsWith("row", StringComparison.Ordinal)
            && int.TryParse(buttonId.AsSpan(3), out int slot)
            && slot >= 1
            && slot <= _rows.Count
        )
        {
            return _rows[slot - 1].Id;
        }

        return buttonId;
    }
}
