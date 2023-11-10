using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace PlayCs;

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
        SendCommands(new[] { "mp_pause_match" });
        Message(HudDestination.Center, $"{player.PlayerName} {ChatColors.Red}paused the match");
    }

    [ConsoleCommand("css_resume", "Resumes the match ")]
    public void onResume(CCSPlayerController? player, CommandInfo? command)
    {
        SendCommands(new[] { "mp_unpause_match" });
        Message(HudDestination.Center, $"{player.PlayerName} {ChatColors.Red}resumed the match");
    }

    [ConsoleCommand("css_timeout", "Tatical Teimout")]
    public void onTimeout(CCSPlayerController? player, CommandInfo? command)
    {
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
    }
}
