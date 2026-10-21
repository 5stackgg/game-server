using System.Diagnostics.CodeAnalysis;

namespace FiveStack.Utilities;

// When a walk of the level's env_cs_place entities is the map's callouts, and
// which of the places it finds belong to it.
//
// The map-load event comes before the new level's places spawn, while the
// outgoing level's can still be in the entity list, whole or half torn down. A
// walk there, or a second later, reads the map being left and files it under the
// new map's name -- and a map that has answered is never walked again. So after a
// map load only places that spawned since count, and nothing is walked until one
// has. A plugin hot-reloaded mid-map saw no map load and nothing is changing: every
// place in the world is the current level's.
public sealed class MapCalloutsWalk
{
    // Spawned places can still all walk as empty (unnamed, or too thin to be an
    // area), and a map that has nothing worth naming must not be walked for ever.
    public const int MaxAttempts = 10;

    // Still filled after the answer: `.callouts` walks the level again much
    // later, and a place respawned in between is under a new index.
    private HashSet<uint>? _spawned;
    private string _reported = string.Empty;
    private int _attempts;

    public void MapLoaded()
    {
        _spawned = new HashSet<uint>();
        _reported = string.Empty;
        _attempts = 0;
    }

    public void Spawned(uint index)
    {
        _spawned?.Add(index);
    }

    /// <summary>
    /// Whether to walk now. Counts as an attempt only when it says yes: waiting
    /// for the new level to spawn costs nothing.
    /// </summary>
    public bool Begin([NotNullWhen(true)] string? map)
    {
        if (
            string.IsNullOrEmpty(map)
            || _reported == map
            || _attempts >= MaxAttempts
            || _spawned is { Count: 0 }
        )
        {
            return false;
        }

        _attempts++;

        return true;
    }

    public bool Counts(uint index)
    {
        return _spawned == null || _spawned.Contains(index);
    }

    public void Answered(string map)
    {
        _reported = map;
    }
}
