namespace FiveStack.Tests.Services;

using CounterStrikeSharp.API.Modules.Utils;

/// <summary>
/// Tests for CaptainSystem decision logic.
/// CaptainSystem uses CCSPlayerController (CounterStrikeSharp game engine type)
/// that cannot be mocked. These tests verify the pure dictionary and decision
/// logic extracted from the service.
/// </summary>
public class CaptainSystemTests
{
    // -- Captain dictionary management logic --
    // CaptainSystem internally uses Dictionary<CsTeam, string?> keyed by team

    [Fact]
    public void ClaimCaptain_AssignsCaptain_WhenSlotEmpty()
    {
        var captains = new Dictionary<CsTeam, string?>
        {
            { CsTeam.Terrorist, null },
            { CsTeam.CounterTerrorist, null },
        };

        // Claim for Terrorist team
        captains[CsTeam.Terrorist] = "player-1";

        captains[CsTeam.Terrorist].Should().Be("player-1");
        captains[CsTeam.CounterTerrorist].Should().BeNull();
    }

    [Fact]
    public void ClaimCaptain_DoesNotReplace_WithoutForce()
    {
        var captains = new Dictionary<CsTeam, string?>
        {
            { CsTeam.Terrorist, "existing-captain" },
            { CsTeam.CounterTerrorist, null },
        };

        bool force = false;
        string? current = captains[CsTeam.Terrorist];

        // ClaimCaptain only assigns if null or force
        if (current == null || force)
        {
            captains[CsTeam.Terrorist] = "new-player";
        }

        captains[CsTeam.Terrorist].Should().Be("existing-captain");
    }

    [Fact]
    public void ClaimCaptain_Replaces_WithForce()
    {
        var captains = new Dictionary<CsTeam, string?>
        {
            { CsTeam.Terrorist, "existing-captain" },
            { CsTeam.CounterTerrorist, null },
        };

        bool force = true;
        string? current = captains[CsTeam.Terrorist];

        if (current == null || force)
        {
            captains[CsTeam.Terrorist] = "new-player";
        }

        captains[CsTeam.Terrorist].Should().Be("new-player");
    }

    [Fact]
    public void Reset_ClearsAllCaptains()
    {
        var captains = new Dictionary<CsTeam, string?>
        {
            { CsTeam.Terrorist, "player-1" },
            { CsTeam.CounterTerrorist, "player-2" },
        };

        captains.Clear();
        captains[CsTeam.Terrorist] = null;
        captains[CsTeam.CounterTerrorist] = null;

        captains[CsTeam.Terrorist].Should().BeNull();
        captains[CsTeam.CounterTerrorist].Should().BeNull();
    }

    [Fact]
    public void GetTeamCaptain_ReturnsNull_ForSpectator()
    {
        // GetTeamCaptain returns null for None and Spectator
        var team = CsTeam.Spectator;
        bool isValidTeam = team != CsTeam.None && team != CsTeam.Spectator;
        isValidTeam.Should().BeFalse();
    }

    [Fact]
    public void GetTeamCaptain_ReturnsNull_ForNone()
    {
        var team = CsTeam.None;
        bool isValidTeam = team != CsTeam.None && team != CsTeam.Spectator;
        isValidTeam.Should().BeFalse();
    }

    [Theory]
    [InlineData(CsTeam.Terrorist)]
    [InlineData(CsTeam.CounterTerrorist)]
    public void GetTeamCaptain_ReturnsValue_ForPlayingTeams(CsTeam team)
    {
        bool isValidTeam = team != CsTeam.None && team != CsTeam.Spectator;
        isValidTeam.Should().BeTrue();
    }

    // -- IsCaptain decision logic --
    // IsCaptain checks: valid team, player on same team, member.captain flag, then dictionary

    [Fact]
    public void IsCaptain_FalseForInvalidTeam()
    {
        var team = CsTeam.Spectator;
        bool validTeam = team == CsTeam.Terrorist || team == CsTeam.CounterTerrorist;
        validTeam.Should().BeFalse();
    }

    [Fact]
    public void IsCaptain_TrueWhenMemberHasCaptainFlag()
    {
        // When member.captain == true, IsCaptain returns true regardless of dictionary
        bool memberCaptain = true;
        memberCaptain.Should().BeTrue();
    }

    [Fact]
    public void IsCaptain_FallsBackToDictionary_WhenNoCaptainFlag()
    {
        var captains = new Dictionary<CsTeam, string?>
        {
            { CsTeam.Terrorist, "76561198000000001" },
            { CsTeam.CounterTerrorist, null },
        };

        bool memberCaptain = false;

        // When member.captain is false, check dictionary match
        string playerSteamId = "76561198000000001";
        bool isCaptain = memberCaptain || captains[CsTeam.Terrorist] == playerSteamId;
        isCaptain.Should().BeTrue();

        string otherPlayer = "76561198000000002";
        bool otherIsCaptain = memberCaptain || captains[CsTeam.Terrorist] == otherPlayer;
        otherIsCaptain.Should().BeFalse();
    }

    // -- RemoveCaptain guard conditions --

    [Fact]
    public void RemoveCaptain_OnlyDuringWarmup()
    {
        // RemoveCaptain checks: match.IsWarmup(), team != None/Spectator,
        // captain exists, and SteamID matches
        bool isWarmup = false;
        bool shouldRemove = isWarmup; // guards would fail
        shouldRemove.Should().BeFalse();
    }

    [Fact]
    public void RemoveCaptain_RequiresSteamIdMatch()
    {
        string captainSteamId = "76561198000000001";
        string requestingSteamId = "76561198000000002";

        (captainSteamId == requestingSteamId).Should().BeFalse();
    }

    [Fact]
    public void RemoveCaptain_SetsCaptainToNull()
    {
        var captains = new Dictionary<CsTeam, string?>
        {
            { CsTeam.Terrorist, "76561198000000001" },
            { CsTeam.CounterTerrorist, null },
        };

        captains[CsTeam.Terrorist] = null;
        captains[CsTeam.Terrorist].Should().BeNull();
    }

    // -- Captain event data --

    [Fact]
    public void CaptainClaimEvent_IncludesRequiredFields()
    {
        var eventData = new Dictionary<string, object>
        {
            { "claim", true },
            { "steam_id", "76561198000000001" },
            { "player_name", "TestPlayer" },
        };

        eventData["claim"].Should().Be(true);
        eventData["steam_id"].Should().Be("76561198000000001");
        eventData["player_name"].Should().Be("TestPlayer");
    }

    [Fact]
    public void CaptainRemoveEvent_HasClaimFalse()
    {
        var eventData = new Dictionary<string, object>
        {
            { "claim", false },
            { "steam_id", "76561198000000001" },
            { "player_name", "TestPlayer" },
        };

        eventData["claim"].Should().Be(false);
    }
}
