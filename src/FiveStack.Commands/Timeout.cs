using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_pause", "Pauses the match")]
    public void OnPause(CCSPlayerController? player, CommandInfo? command)
    {
        _matchTimeouts.CallTimeout(player);
    }

    [ConsoleCommand("css_resume", "Resumes the match")]
    public void OnResume(CCSPlayerController? player, CommandInfo? command)
    {
        _matchTimeouts.Resume(player);
    }

    [ConsoleCommand("css_tac", "Tactical Timeout")]
    public void OnTimeout(CCSPlayerController? player, CommandInfo? command)
    {
        _matchTimeouts.CallTacTimeout(player);
    }
}
