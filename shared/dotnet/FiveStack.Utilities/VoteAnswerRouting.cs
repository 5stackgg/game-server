namespace FiveStack.Utilities;

public static class VoteAnswerRouting
{
    public static (IReadOnlyList<string> Targets, IReadOnlyList<string> Choices) Route(
        IReadOnlyList<(string Name, bool CanAnswer)> openVotes,
        string? named
    )
    {
        return (openVotes.Select(vote => vote.Name).ToList(), new List<string>());
    }
}
