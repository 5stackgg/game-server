using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using PlayCs.entities;
using PlayCS.enums;

namespace PlayCs;

public partial class PlayCsPlugin
{
    private void CaptureRoundEnd()
    {
        RegisterEventHandler<EventGameEnd>(
            (@event, info) =>
            {
                UpdatePhase(ePhase.Finished);

                return HookResult.Continue;
            }
        );

        RegisterEventHandler<EventRoundOfficiallyEnded>(
            (@event, info) =>
            {
                Console.WriteLine("OFFCIALLY ENDED");
                UpdateCurrentRound();

                return HookResult.Continue;
            }
        );

        RegisterEventHandler<EventRoundEnd>(
            (@event, info) =>
            {
                if (_matchData == null || _currentPhase == ePhase.Knife)
                {
                    Console.WriteLine($"TEAM ASSIGNED {@event.Winner}");

                    _knifeWinningTeam = TeamNumToCSTeam(@event.Winner);

                    NotifyCaptainSideSelection();

                    return HookResult.Continue;
                }

                _redis.PublishMatchEvent(
                    _matchData.id,
                    new Redis.EventData<Dictionary<string, object>>
                    {
                        @event = "score",
                        data = new Dictionary<string, object>
                        {
                            { "round", _currentRound + 1 },
                            { "team_1_score", $"{GetTeamScore(1)}" },
                            { "team_2_score", $"{GetTeamScore(2)}" },
                        }
                    }
                );

                return HookResult.Continue;
            }
        );
    }

    public int GetTeamScore(int teamNumber)
    {
        if (_matchData == null)
        {
            return 0;
        }

        var teamManagers = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");

        foreach (var teamManager in teamManagers)
        {
            MatchTeam? team = _matchData
                .teams
                .Find(
                    (_team) =>
                    {
                        return _team.name == teamManager.ClanTeamname;
                    }
                );

            if (team != null && team.team_number == teamNumber)
            {
                return teamManager.Score;
            }
        }

        return 0;
    }

    public void UpdateCurrentRound()
    {
        int roundsPlayed = 0;
        var teamManagers = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");

        foreach (var teamManager in teamManagers)
        {
            if (
                teamManager.TeamNum == (int)CsTeam.Terrorist
                || teamManager.TeamNum == (int)CsTeam.CounterTerrorist
            )
            {
                roundsPlayed += teamManager.Score;
            }
        }

        _currentRound = roundsPlayed;
    }
}
