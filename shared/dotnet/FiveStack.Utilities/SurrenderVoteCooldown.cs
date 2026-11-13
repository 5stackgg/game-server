namespace FiveStack.Utilities;

public class SurrenderVoteCooldown
{
    public void Failed(Guid mapId, Guid lineupId, int round) { }

    public bool Blocks(Guid mapId, Guid lineupId, int round)
    {
        return false;
    }
}
