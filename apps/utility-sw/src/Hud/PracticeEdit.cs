using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using static SwiftlyS2.Shared.Helper;

namespace UtilityPractice;

public partial class UtilityPracticePlugin
{
    // A draft, not the lineup. Edits accumulate here and go in one request when
    // SAVE is pressed, so a player who changes their mind costs nothing and a
    // half-finished edit never reaches the panel.
    private class EditDraft
    {
        public required string LineupId { get; init; }
        public required string ClientId { get; init; }
        public required string Name { get; set; }
        public string? Description { get; set; }
        public required string Visibility { get; set; }
        public string? Asking { get; set; }
        public string Hint { get; set; } = "";
        public string Tone { get; set; } = "";

        // What the map would call it, when that differs from the current name.
        public string? Suggestion { get; set; }
    }

    private readonly Dictionary<ulong, EditDraft> _edits = new();

    private bool OpenEdit(IPlayer player, LineupRecord lineup)
    {
        if (!UseHud(player.SteamID) || !_hud!.Available(HudSlots.Edit))
        {
            return false;
        }

        // Without a panel id there is nothing to PATCH: the lineup exists only
        // in this session and has never been ingested.
        if (string.IsNullOrEmpty(lineup.id))
        {
            Tell(
                player.SteamID,
                $" {ChatColors.Red}that lineup has not reached the panel yet, so it cannot be edited"
            );

            return true;
        }

        _edits[player.SteamID] = new EditDraft
        {
            LineupId = lineup.id!,
            ClientId = lineup.client_id,
            Name = lineup.name,
            Description = lineup.description,
            Visibility = (lineup.visibility ?? nameof(eLineupVisibility.Private)).ToLowerInvariant(),
            Hint = "click a field to change it",
        };

        // Offered, never applied: a lineup somebody named by hand keeps that
        // name until they ask for the other one.
        string automatic = LineupNaming.Auto(
            lineup.utility_type,
            lineup.release.feet_position,
            lineup.detonation_position,
            _callouts.Callouts
        );

        if (automatic.Length > 0 && automatic != lineup.name)
        {
            _edits[player.SteamID].Suggestion = automatic;
            _edits[player.SteamID].Hint = $"map calls it \"{automatic}\"";
        }

        return RenderEdit(player);
    }

    private bool RenderEdit(IPlayer player)
    {
        if (_hud == null || !_edits.TryGetValue(player.SteamID, out EditDraft? draft))
        {
            return false;
        }

        return _hud.Show(
            player,
            new NadeEditTemplate
            {
                Tag = $"{_library.Map} · edit",
                Title = PracticeLineupUtility.TitleCase(draft.Name),
                Name = draft.Name,
                Description = draft.Description,
                Visibility = draft.Visibility,
                Hint = draft.Hint,
                HintTone = draft.Tone,
                Asking = draft.Asking,
            }
        );
    }

    private void CloseEdit(IPlayer player)
    {
        _prompt.Cancel(player.PlayerID);
        _edits.Remove(player.SteamID);
        _hud?.Hide(player, HudSlots.Edit);
    }

    private void OnEditClicked(IPlayer player, string button)
    {
        if (!_edits.TryGetValue(player.SteamID, out EditDraft? draft))
        {
            return;
        }

        ulong steamId = player.SteamID;

        switch (button)
        {
            case "close":
                CloseEdit(player);

                return;

            case "name" when draft.Suggestion != null:
                draft.Name = draft.Suggestion;
                draft.Suggestion = null;
                draft.Hint = "unsaved changes";
                draft.Tone = "";
                RenderEdit(player);

                return;

            case "name":
            case "desc":
                draft.Asking = button;
                draft.Tone = "warn";
                draft.Hint =
                    button == "name" ? "type the new name in chat" : "type the write-up in chat";

                _prompt.Ask(
                    player.PlayerID,
                    answer =>
                    {
                        if (!_edits.TryGetValue(steamId, out EditDraft? open))
                        {
                            return;
                        }

                        if (button == "name")
                        {
                            open.Name = answer;
                        }
                        else
                        {
                            open.Description = answer;
                        }

                        open.Asking = null;
                        open.Tone = "";
                        open.Hint = "unsaved changes";

                        IPlayer? still = _system.Find(steamId);

                        if (still != null && still.IsValid)
                        {
                            RenderEdit(still);
                        }
                    }
                );

                RenderEdit(player);

                return;

            case "revert":
                CloseEdit(player);
                Tell(steamId, $" {ChatColors.Grey}edit discarded");

                return;

            case "save":
                SaveEdit(player, draft);

                return;
        }

        if (button.StartsWith("vis_", StringComparison.Ordinal))
        {
            draft.Visibility = button["vis_".Length..];
            draft.Hint = "unsaved changes";
            draft.Tone = "";
            RenderEdit(player);
        }
    }

    private void SaveEdit(IPlayer player, EditDraft draft)
    {
        ulong steamId = player.SteamID;

        draft.Hint = "saving...";
        draft.Tone = "";
        RenderEdit(player);

        _ = Task.Run(async () =>
        {
            UtilityApiClient.EditResult result = await _api.Update(
                draft.LineupId,
                steamId,
                draft.Name,
                draft.Description,
                draft.Visibility
            );

            Core.Scheduler.NextTick(() =>
            {
                IPlayer? still = _system.Find(steamId);

                if (still == null || !still.IsValid)
                {
                    return;
                }

                if (result.Outcome != UtilityApiClient.eEditOutcome.Saved)
                {
                    if (_edits.TryGetValue(steamId, out EditDraft? open))
                    {
                        // Each refusal is something different to do next, so
                        // none of them collapse into "it failed".
                        open.Hint = result.Outcome switch
                        {
                            UtilityApiClient.eEditOutcome.NotYours
                                => "this lineup belongs to somebody else",
                            UtilityApiClient.eEditOutcome.AlreadyPractised
                                => "others have practised this - save a copy instead",
                            _ => "the panel would not take that edit",
                        };
                        open.Tone = "bad";
                        RenderEdit(still);
                    }

                    return;
                }

                // The library is what every other surface reads, so it has to
                // agree before the panel is told anything succeeded.
                LineupRecord? row = _library
                    .For(steamId)
                    .FirstOrDefault(l => l.client_id == draft.ClientId);

                if (row != null)
                {
                    row.name = draft.Name;
                    row.description = draft.Description;
                    row.visibility = draft.Visibility;
                }

                CloseEdit(still);
                Tell(steamId, $" {ChatColors.Green}saved {ChatColors.Default}{draft.Name}");

                // Their hit rate on this lineup just went to zero; finding that
                // out from a drill later would read as a bug.
                if (result.ProgressReset)
                {
                    Tell(
                        steamId,
                        $" {ChatColors.Grey}your record on it was cleared - it is a different throw now"
                    );
                }
            });
        });
    }

    [Command("edit", registerRaw: false, permission: "")]
    public void OnEdit(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        LineupRecord? loaded = _system.StateFor(player.SteamID).Loaded;

        if (loaded == null)
        {
            Reply(context, $" {ChatColors.Red}load a lineup first");

            return;
        }

        if (!OpenEdit(player, loaded))
        {
            Reply(
                context,
                $" {ChatColors.Red}editing needs the 5stack HUD addon"
            );
        }
    }
}
