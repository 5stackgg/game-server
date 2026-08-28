using FiveStack.Utilities;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace UtilityPractice;

// One player's view of a shared custom_hud_layout entity. Every write goes
// through a ...ForPlayer call, so a single entity serves the whole server with
// independent content.
public class HudSurface
{
    private readonly CCSCustomHudLayout _layout;
    private readonly HudLayoutSlots _slots;
    private readonly int _playerId;

    private readonly Dictionary<string, string> _variables = new();
    private readonly Dictionary<(string, string), bool> _classes = new();

    public HudSurface(CCSCustomHudLayout layout, HudLayoutSlots slots, int playerId)
    {
        _layout = layout;
        _slots = slots;
        _playerId = playerId;
    }

    public CCSCustomHudLayout Layout => _layout;

    public void Set(string name, string value)
    {
        if (_variables.TryGetValue(name, out string? showing) && showing == value)
        {
            return;
        }

        _variables[name] = value;
        _layout.SetDialogVariableStringForPlayer(_playerId, _slots.ElementFor(name), name, value);
    }

    public void Class(string elementId, string name, bool on)
    {
        (string, string) key = (elementId, name);

        if (_classes.TryGetValue(key, out bool was) && was == on)
        {
            return;
        }

        _classes[key] = on;
        _layout.SetHasClassForPlayer(
            _playerId,
            elementId,
            name,
            on
                ? EHudPanelClassStatus_t.k_eHudPanelClassStatus_HasClass
                : EHudPanelClassStatus_t.k_eHudPanelClassStatus_DoesNotHaveClass
        );
    }

    // A position Panorama can render: exactly one of prefix0..prefix(count-1)
    // is present. Clearing the previous one first, so the two are never both
    // set even for a frame.
    public void Pick(string elementId, string prefix, int index, int count)
    {
        int chosen = Math.Clamp(index, 0, count - 1);

        for (int step = 0; step < count; step++)
        {
            if (step != chosen)
            {
                Class(elementId, $"{prefix}{step}", false);
            }
        }

        Class(elementId, $"{prefix}{chosen}", true);
    }

    public void Show(bool on)
    {
        Class(_slots.RootId, HudSlots.Shown, on);
    }

    public void Input(bool on)
    {
        _layout.SetInputCaptureEnabledForPlayer(_playerId, on);
    }

    // The diff above assumes the client is holding what we last sent it. A
    // respawned entity or a reconnected player is holding nothing, so the cache
    // has to be dropped or every unchanged value is suppressed forever.
    public void Invalidate()
    {
        _variables.Clear();
        _classes.Clear();
    }
}
