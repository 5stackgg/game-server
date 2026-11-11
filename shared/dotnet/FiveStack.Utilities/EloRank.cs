using FiveStack.Entities;

namespace FiveStack.Utilities
{
    // CS2 shows the Premier stake as the predicted rating minus the current one,
    // so the predicted fields hold absolute ratings. A delta the api did not send
    // keeps the predicted rating at the current one (stake 0).
    public readonly record struct EloRank(int Elo, int Win, int Loss, int Tie)
    {
        public static EloRank? From(MatchMember member)
        {
            if (!member.elo.HasValue)
            {
                return null;
            }

            int elo = member.elo.Value;

            return new EloRank(
                elo,
                elo + (member.elo_win ?? 0),
                elo + (member.elo_loss ?? 0),
                elo + (member.elo_tie ?? 0)
            );
        }
    }
}
