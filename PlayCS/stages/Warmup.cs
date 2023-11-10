using CounterStrikeSharp.API.Modules.Utils;

namespace PlayCs;

public partial class PlayCsPlugin
{
    public void startWarmup()
    {
        Console.WriteLine($"I AM PAHSE {Phase}");
        if (Phase != ePhase.Unknown && Phase != ePhase.Knife && Phase != ePhase.Scheduled)
        {
            return;
        }

        Message(HudDestination.Center, "Starting Warmup!");

        // mp_warmup_start -- should be able todo this easily
        // TODO - detect warmup
        SendCommands(
            new[]
            {
                "mp_autokick 0",
                "mp_limitteams 0",
                "mp_autoteambalance 0",
                "sv_disable_teamselect_menu 0",
                "mp_warmuptime_all_players_connected 0",
                "mp_roundtime 60",
                "mp_roundtime_defuse 0",
                "mp_ct_default_secondary weapon_hkp2000",
                "mp_t_default_secondary weapon_glock",
                "mp_give_player_c4 0",
                "mp_warmup_pausetimer 1",
                "mp_maxmoney 60000",
                "mp_startmoney 60000",
                "mp_free_armor 1",
                // "mp_warmup_start",
                // "mp_restartgame 3"
            }
        );

        UpdatePhase(ePhase.Warmup);

        // await this.setupTeamNames(matchId);
        // await UpdateStatus(matchId, EMatchStatusEnum.Warmup);
    }
}
