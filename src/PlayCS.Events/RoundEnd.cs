using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using PlayCs.entities;
using PlayCS.enums;

namespace PlayCs;

public partial class PlayCsPlugin
{
    [GameEventHandler]
    public HookResult OnGameEnd(EventGameEnd @event, GameEventInfo info)
    {
        UpdateGameState(eGameState.Finished);

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundOfficallyOver(EventRoundOfficiallyEnded @event, GameEventInfo info)
    {
        UpdateCurrentRound();

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundOver(EventRoundEnd @event, GameEventInfo info)
    {
        if (_matchData == null || _currentGameState == eGameState.Knife)
        {
            Console.WriteLine($"TEAM ASSIGNED {@event.Winner}");

            KnifeWinningTeam = TeamNumToCSTeam(@event.Winner);

            _NotifyCaptainSideSelection();

            return HookResult.Continue;
        }

        if (!IsLive())
        {
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

    public void _NotifyCaptainSideSelection()
    {
        if (KnifeWinningTeam == null)
        {
            return;
        }

        CsTeam knifeTeam =
            KnifeWinningTeam == CsTeam.Terrorist ? CsTeam.Terrorist : CsTeam.CounterTerrorist;

        Message(
            HudDestination.Chat,
            $"As the captain you must select to {ChatColors.Green}!stay {ChatColors.Default} or {ChatColors.Green}!switch",
            _captains[knifeTeam]
        );
        Message(
            HudDestination.Alert,
            $"{(KnifeWinningTeam == CsTeam.Terrorist ? "Terrorist" : "CT")} - Captain is Picking Sides!"
        );
    }
}
