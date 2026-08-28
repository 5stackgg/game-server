using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Misc;

namespace UtilityPractice;

// The text input a Panorama panel cannot have.
//
// custom_hud_layout gives buttons and class toggles and nothing else -- there is
// no way to type into it. But there is a text field two inches below the panel,
// so a prompt is: ask on the panel, capture the player's next chat line, and
// swallow it so the server does not see them shout a lineup name at everyone.
//
// One outstanding prompt per player. Asking again replaces the first, because a
// queue of half-answered questions is worse than losing one.
public class HudPrompt
{
    private readonly ISwiftlyCore _core;
    private readonly ILogger<HudPrompt> _logger;

    private readonly Dictionary<int, Pending> _waiting = new();
    private Guid? _hook;

    public HudPrompt(ISwiftlyCore core, ILogger<HudPrompt> logger)
    {
        _core = core;
        _logger = logger;
    }

    private class Pending
    {
        public required Action<string> Answered { get; init; }
        public required DateTime Expires { get; init; }
    }

    // Long enough to think of a name, short enough that a forgotten prompt does
    // not eat a chat message ten minutes later.
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(45);

    public void Start()
    {
        _hook ??= _core.Command.HookClientChat(OnChat);
    }

    public void Stop()
    {
        if (_hook is { } id)
        {
            _core.Command.UnhookClientChat(id);
            _hook = null;
        }

        _waiting.Clear();
    }

    public void Ask(int playerId, Action<string> answered)
    {
        _waiting[playerId] = new Pending
        {
            Answered = answered,
            Expires = DateTime.UtcNow + Window,
        };
    }

    public bool Waiting(int playerId)
    {
        return _waiting.ContainsKey(playerId);
    }

    public void Cancel(int playerId)
    {
        _waiting.Remove(playerId);
    }

    private HookResult OnChat(int playerId, string text, bool teamOnly)
    {
        if (!_waiting.TryGetValue(playerId, out Pending? pending))
        {
            return HookResult.Continue;
        }

        _waiting.Remove(playerId);

        if (DateTime.UtcNow > pending.Expires)
        {
            return HookResult.Continue;
        }

        string answer = text.Trim();

        // Their own commands still work while a prompt is open: somebody who
        // types .menu instead of answering wanted the menu, not a lineup called
        // ".menu".
        if (answer.Length == 0 || answer.StartsWith('.') || answer.StartsWith('!'))
        {
            return HookResult.Continue;
        }

        try
        {
            pending.Answered(answer);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "hud prompt handler threw");
        }

        // Swallowed: they were answering the panel, not talking to the server.
        return HookResult.Stop;
    }
}
