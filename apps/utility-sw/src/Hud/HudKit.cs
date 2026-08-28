using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace UtilityPractice;

// Drives the plugin's custom_hud_layout entities: one entity per layout for the
// whole server, one surface per player per layout, and click routing back out.
//
// Every entry point returns whether the HUD actually took the work, because the
// caller still owns a working centre-text path and has to know when to use it.
public class HudKit
{
    private readonly ISwiftlyCore _core;
    private readonly ILogger<HudKit> _logger;

    private readonly Dictionary<string, CCSCustomHudLayout> _entities = new();
    private readonly Dictionary<string, DateTime> _retryAfter = new();
    private readonly HashSet<string> _warned = new();
    private readonly Dictionary<int, Dictionary<string, Open>> _open = new();

    // Long enough that a server whose addon is not mounted is not paying for a
    // failed spawn on every frame, short enough that a layout which only failed
    // because the map had not finished loading comes back on its own.
    private static readonly TimeSpan RetryGap = TimeSpan.FromSeconds(30);

    public HudKit(ISwiftlyCore core, ILogger<HudKit> logger)
    {
        _core = core;
        _logger = logger;
    }

    public event Action<IPlayer, string, string>? Clicked;

    private class Open
    {
        public required HudSurface Surface { get; init; }
        public required HudTemplate Template { get; set; }
        public required uint EntityIndex { get; set; }
        public bool Visible { get; set; }
    }

    public bool Show(IPlayer player, HudTemplate template)
    {
        Open? open = Surface(player, template);

        if (open == null)
        {
            return false;
        }

        open.Template = template;
        template.Apply(open.Surface);

        if (!open.Visible)
        {
            open.Visible = true;
            open.Surface.Show(true);

            if (template.CapturesInput)
            {
                open.Surface.Input(true);
            }
        }

        return true;
    }

    public void Hide(IPlayer player, HudLayoutSlots slots)
    {
        if (
            !_open.TryGetValue(player.PlayerID, out Dictionary<string, Open>? layouts)
            || !layouts.TryGetValue(slots.Layout, out Open? open)
            || !open.Visible
        )
        {
            return;
        }

        open.Visible = false;
        open.Surface.Show(false);

        if (open.Template.CapturesInput)
        {
            open.Surface.Input(false);
        }
    }

    public bool IsOpen(IPlayer player, HudLayoutSlots slots)
    {
        return _open.TryGetValue(player.PlayerID, out Dictionary<string, Open>? layouts)
            && layouts.TryGetValue(slots.Layout, out Open? open)
            && open.Visible;
    }

    // Whether this layout can be driven at all. Callers with a fallback ask
    // before building a template they may not be able to show.
    public bool Available(HudLayoutSlots slots)
    {
        return Entity(slots) != null;
    }

    public void Forget(int playerId)
    {
        _open.Remove(playerId);
    }

    // Called on unload, INCLUDING a hot reload. Without this the entities outlive
    // the plugin: the next load spawns a second set, players keep a captured
    // cursor pointing at handles nothing owns any more, and the only way out is
    // a map change.
    //
    // Order matters. Input capture and visibility are released while the
    // entities are still alive, because afterwards there is nothing to send the
    // release through.
    public void Shutdown()
    {
        foreach (KeyValuePair<int, Dictionary<string, Open>> viewer in _open)
        {
            foreach (Open open in viewer.Value.Values)
            {
                if (!open.Surface.Layout.IsValid)
                {
                    continue;
                }

                try
                {
                    open.Surface.Input(false);
                    open.Surface.Show(false);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "could not release hud state for player {Player}",
                        viewer.Key
                    );
                }
            }
        }

        foreach (CCSCustomHudLayout entity in _entities.Values)
        {
            if (!entity.IsValid)
            {
                continue;
            }

            try
            {
                entity.Despawn();
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "could not despawn a custom hud layout");
            }
        }

        Reset();
    }

    // The entities do not survive a changelevel and neither does anything the
    // clients were holding, so both the handles and every diff cache go.
    public void Reset()
    {
        _entities.Clear();
        _retryAfter.Clear();
        _warned.Clear();
        _open.Clear();
    }

    public void OnClicked(IOnCustomHudClickedEvent @event)
    {
        IPlayer? player = _core.PlayerManager.GetPlayer(@event.PlayerId);

        if (player == null || !player.IsValid || player.IsFakeClient)
        {
            return;
        }

        if (!_open.TryGetValue(@event.PlayerId, out Dictionary<string, Open>? layouts))
        {
            return;
        }

        uint index = EntityIndex(@event.CustomHudLayout.Index);

        foreach (KeyValuePair<string, Open> entry in layouts)
        {
            if (EntityIndex(entry.Value.EntityIndex) != index || !entry.Value.Visible)
            {
                continue;
            }

            string resolved = entry.Value.Template.Resolve(@event.ButtonId);

            try
            {
                Clicked?.Invoke(player, entry.Key, resolved);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "hud click handler threw for {Layout} button {Button}",
                    entry.Key,
                    resolved
                );
            }

            return;
        }
    }

    // The click carries a packed CHandle in some builds and a bare index in
    // others; the low 14 bits are the entity index either way, so masking both
    // sides compares equal under both.
    private static uint EntityIndex(uint indexOrHandle)
    {
        return indexOrHandle & 0x3FFF;
    }

    private Open? Surface(IPlayer player, HudTemplate template)
    {
        HudLayoutSlots slots = template.Slots;
        CCSCustomHudLayout? entity = Entity(slots);

        if (entity == null)
        {
            return null;
        }

        if (!_open.TryGetValue(player.PlayerID, out Dictionary<string, Open>? layouts))
        {
            layouts = new Dictionary<string, Open>();
            _open[player.PlayerID] = layouts;
        }

        if (layouts.TryGetValue(slots.Layout, out Open? open))
        {
            if (open.EntityIndex == entity.Index)
            {
                return open;
            }

            // Same layout, new entity: the client is holding nothing we sent to
            // the old one.
            open.Surface.Invalidate();
        }

        open = new Open
        {
            Surface = new HudSurface(entity, slots, player.PlayerID),
            Template = template,
            EntityIndex = entity.Index,
        };

        layouts[slots.Layout] = open;

        return open;
    }

    private CCSCustomHudLayout? Entity(HudLayoutSlots slots)
    {
        if (_entities.TryGetValue(slots.Layout, out CCSCustomHudLayout? entity) && entity.IsValid)
        {
            return entity;
        }

        if (_retryAfter.TryGetValue(slots.Layout, out DateTime next) && DateTime.UtcNow < next)
        {
            return null;
        }

        try
        {
            CCSCustomHudLayout created = _core.EntitySystem.CreateEntity<CCSCustomHudLayout>();

            // The COMPILED name. The docs example says .xml; both working
            // implementations say .vxml_c, and a path that resolves to nothing
            // spawns an entity that renders nothing.
            created.StrLayout = $"panorama/layout/custom_game/{slots.Layout}.vxml_c";
            created.StrLayoutUpdated();
            created.DispatchSpawn();

            if (!created.IsValid)
            {
                throw new InvalidOperationException("entity did not survive spawn");
            }

            _entities[slots.Layout] = created;
            _retryAfter.Remove(slots.Layout);

            return created;
        }
        catch (Exception exception)
        {
            _retryAfter[slots.Layout] = DateTime.UtcNow + RetryGap;

            // Said once. On a server with no addon mounted this is the steady
            // state, not an incident.
            if (_warned.Add(slots.Layout))
            {
                _logger.LogWarning(
                    exception,
                    "custom hud layout {Layout} unavailable, falling back to centre text",
                    slots.Layout
                );
            }

            return null;
        }
    }
}
