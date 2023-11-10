using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace PlayCs;

public partial class PlayCsPlugin
{
    private Dictionary<int, bool> ReadyPlayers = new Dictionary<int, bool>();

    public void startWarmup()
    {
        if (
            CurrentPhase != ePhase.Unknown
            && CurrentPhase != ePhase.Knife
            && CurrentPhase != ePhase.Scheduled
        )
        {
            return;
        }

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
                "mp_warmup_start",
            }
        );

        // await this.setupTeamNames(matchId);
    }

    private bool IsWarmup()
    {
        CCSGameRules? rules = GameRules();

        if (rules == null)
        {
            return false;
        }

        return rules.WarmupPeriod;
    }

    private int TotalReady()
    {
        return ReadyPlayers.Count(pair => pair.Value);
    }

    private CCSGameRules? GameRules()
    {
        return Utilities
            .FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
            .First()
            .GameRules;
    }
}

// private async setupTeamNames(matchId: string) {
//    const { v_match_team_captains: captains } = await this.graphQL.query({
//      v_match_team_captains: [
//        {
//          where: {
//            match_id: {
//              _eq: matchId,
//            },
//          },
//        },
//        {
//          id: true,
//          name: true,
//          team: {
//            name: true,
//            starting_side: true,
//          },
//        },
//      ],
//    });
//
//    for (const captain of captains) {
//      let teamNumber;
//
//      const teamName = `Team ${captain.name}`;
//      if (captain.team.starting_side === e_sides_enum.CT) {
//        teamNumber = 1;
//      } else {
//        teamNumber = 2;
//      }
//      await this.command(matchId, `mp_teamname_${teamNumber} ${teamName}`);
//    }
//  }
