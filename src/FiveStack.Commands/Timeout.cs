using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [
        ConsoleCommand("css_tech", "Pauses the match"),
        ConsoleCommand("css_pause", "Pauses the match")
    ]
    public void OnPause(CCSPlayerController? player, CommandInfo? command)
    {
        _timeoutSystem.RequestPause(player);
    }

    [ConsoleCommand("css_resume", "Resumes the match")]
    public void OnResume(CCSPlayerController? player, CommandInfo? command)
    {
        _timeoutSystem.RequestResume(player);
    }

    [
        ConsoleCommand("css_tac", "Tactical Timeout"),
        ConsoleCommand("css_timeout", "Tactical Timeout")
    ]
    public void OnTimeout(CCSPlayerController? player, CommandInfo? command)
    {
        _timeoutSystem.CallTacTimeout(player);
    }
}
