using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_pause", "Pauses the match")]
    public void OnPause(CCSPlayerController? player, CommandInfo? command)
    {
        _matchTimeoutSystem.CallTimeout(player);
    }

    [ConsoleCommand("css_resume", "Resumes the match")]
    public void OnResume(CCSPlayerController? player, CommandInfo? command)
    {
        _matchTimeoutSystem.Resume(player);
    }

    [ConsoleCommand("css_tac", "Tactical Timeout")]
    public void OnTimeout(CCSPlayerController? player, CommandInfo? command)
    {
        _matchTimeoutSystem.CallTacTimeout(player);
    }
}
