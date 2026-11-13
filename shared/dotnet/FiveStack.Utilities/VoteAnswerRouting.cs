namespace FiveStack.Utilities;

public static class VoteAnswerRouting
{
    // A .y or .n goes to the one open vote the player can answer. A resume and a
    // surrender vote can be open at once, so with several it counts for none until
    // the player names one, and the names to choose from come back as Choices.
    // An answer only one vote could be meant for still goes to it when the player
    // cannot answer it, so the vote itself says why (a captain-only vote does).
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

            return openVotes.Any(vote => vote.Name == wanted)
                ? (new List<string> { wanted }, new List<string>())
                : (new List<string>(), new List<string>());
        }

        if (answerable.Count == 0 && openVotes.Count == 1)
        {
            return (new List<string> { openVotes[0].Name }, new List<string>());
        }

        if (answerable.Count > 1)
        {
            return (new List<string>(), answerable);
        }

        return (answerable, new List<string>());
    }
}
