namespace FiveStack.Utilities;

// A lineup that has just voted not to surrender is not asked again on the same
// map until another round has been played.
public class SurrenderVoteCooldown
{
    private (Guid MapId, Guid LineupId, int Round)? _failed;

    public void Failed(Guid mapId, Guid lineupId, int round)
    {
        _failed = (mapId, lineupId, round);
    }

    public bool Blocks(Guid mapId, Guid lineupId, int round)
    {
        return _failed == (mapId, lineupId, round);
    }
}
