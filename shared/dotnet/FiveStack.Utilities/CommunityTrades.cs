namespace FiveStack.Utilities;

// The round's kills, kept long enough to tell a trade: killing the killer of a
// teammate within 5 s of that teammate's death, in the same round, as the
// match stats' trade_pairs count it. Each pair of a teammate's death and the
// kill that traded it counts once, so killing someone who has just killed two
// teammates trades both, and a killer who dies any way at all has paid for
// the kills they made: where players respawn, killing them again is no second
// trade of the same teammate. Players are told apart by slot, as CountsAsKill does,
// and the time is the game clock, which only means anything within a map.
public class CommunityTrades
{
    public const float Window = 5f;

    private sealed record Kill(int Killer, int Victim, int VictimTeam, float At);

    private readonly List<Kill> _kills = new();

    // Returns how many teammates' deaths this kill trades, and keeps the kill,
    // which can be traded in turn. The victim's own kills are traded now.
    public int Killed(int killer, int killerTeam, int victim, int victimTeam, float at)
    {
        _kills.RemoveAll(kill => at - kill.At > Window);

        int traded = _kills.Count(kill =>
            kill.Killer == victim
            && kill.VictimTeam == killerTeam
            && kill.Victim != killer
            && kill.At <= at
        );

        Died(victim);
        _kills.Add(new Kill(killer, victim, victimTeam, at));

        return traded;
    }

    // Any death that is not a counted kill: by the world, the bomb, a bot or a
    // teammate.
    public void Died(int victim)
    {
        _kills.RemoveAll(kill => kill.Killer == victim);
    }

    public void Clear()
    {
        _kills.Clear();
    }
}
