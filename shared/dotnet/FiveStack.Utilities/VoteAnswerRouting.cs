namespace FiveStack.Utilities;

public static class VoteAnswerRouting
{
    // A .y or .n goes to the one open vote the player can answer. A resume and a
    // surrender vote can be open at once, so with several it counts for none until
    // the player names one, and the names to choose from come back as Choices.
    public static (IReadOnlyList<string> Targets, IReadOnlyList<string> Choices) Route(
        IReadOnlyList<(string Name, bool CanAnswer)> openVotes,
        string? named
    )
    {
        List<string> answerable = openVotes
            .Where(vote => vote.CanAnswer)
            .Select(vote => vote.Name)
            .ToList();

        if (!string.IsNullOrWhiteSpace(named))
        {
            string wanted = named.Trim().ToLowerInvariant();

            return answerable.Contains(wanted)
                ? (new List<string> { wanted }, new List<string>())
                : (new List<string>(), new List<string>());
        }

        if (answerable.Count > 1)
        {
            return (new List<string>(), answerable);
        }

        return (answerable, new List<string>());
    }
}
