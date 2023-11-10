using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace PlayCs;

/**
 * Pause needs full .r by at least 1 person on each side (technical timeouts)
 */
public partial class PlayCsPlugin
{
    private Dictionary<CsTeam, int> TeamTimeouts = new Dictionary<CsTeam, int>
    {
        { CsTeam.Terrorist, 0 },
        { CsTeam.CounterTerrorist, 0 }
    };

    [ConsoleCommand("css_pause", "Pauses the match")]
    public void onPause(CCSPlayerController? player, CommandInfo? command)
    {
        // TODO - send up which team / person called timeout

        SendCommands(new[] { "mp_pause_match" });
        Message(HudDestination.Center, $"{player.PlayerName} {ChatColors.Red}paused the match");

        UpdatePhase(ePhase.Paused);
    }

    [ConsoleCommand("css_resume", "Resumes the match ")]
    public void onResume(CCSPlayerController? player, CommandInfo? command)
    {
        // TODO - send up which team / person called timeout
        SendCommands(new[] { "mp_unpause_match" });
        Message(HudDestination.Center, $"{player.PlayerName} {ChatColors.Red}resumed the match");

        // TODO - detect if its in overtime
        UpdatePhase(ePhase.Live);
    }

    [ConsoleCommand("css_timeout", "Tactical Timeout")]
    public void onTimeout(CCSPlayerController? player, CommandInfo? command)
    {
        // TODO - send up which team / person called timeout

        CsTeam team = TeamNumToCSTeam(player.TeamNum);

        if (TeamTimeouts[team] > 4)
        {
            Message(HudDestination.Chat, $"Your team has used all its timeouts!", player);
            return;
        }

        TeamTimeouts[team]++;

        SendCommands(new[] { $"timeout_{(team == CsTeam.Terrorist ? "terrorist" : "ct")}_start" });
        Message(
            HudDestination.Center,
            $"{player.PlayerName} {ChatColors.Red}called a tactical timeout ({TeamTimeouts[team]}/4)"
        );

        UpdatePhase(ePhase.TechTimeout);
    }
}
