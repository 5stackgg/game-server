# Test Coverage — Game Server

## Overview

- **Framework:** xUnit + Moq + FluentAssertions
- **Language:** C# / .NET 8
- **Test command:** `cd tests/FiveStack.Tests && dotnet test --collect:"XPlat Code Coverage"`
- **Test project:** `tests/FiveStack.Tests/`

---

## Test Infrastructure

#### `tests/FiveStack.Tests/Mocks/TestDataFactory.cs` — Entity Factories
Provides factory methods for creating test instances of game entities with configurable parameters. Used across all test files.
- `CreateMatchData()` — match with configurable type, status, teams
- `CreateMatchMap()` — map with configurable map name, team scores, status
- `CreateBackupRound()` — backup round with configurable round number, file path
- `CreateMatchMember()` — team member with configurable Steam ID, captain flag

---

## Unit Tests

### Utilities

#### `tests/FiveStack.Tests/Utilities/TeamUtilityTests.cs` — Team Helpers (5 tests)
Tests utility methods for team identification and mapping.
- Maps team number to team side (CT/T)
- Maps team side to team number
- Handles team swap after half
- Identifies spectator team correctly
- Handles invalid team numbers

#### `tests/FiveStack.Tests/Utilities/MatchUtilityTests.cs` — Match Helpers (2 tests)
Tests match-level utility functions.
- Determines if match is in overtime based on round count
- Calculates correct overtime period number

#### `tests/FiveStack.Tests/Utilities/EnumConversionTests.cs` — Enum Parsing (6 tests)
Tests safe enum conversion between string values and C# enums.
- Parses valid enum string to enum value
- Handles case-insensitive parsing
- Returns default for invalid enum string
- Handles null input
- Handles empty string input
- Parses numeric string representation

---

### Services

#### `tests/FiveStack.Tests/Services/VoteThresholdTests.cs` — Vote System (5 tests)
Tests the vote threshold calculation for in-game votes (surrender, timeout, pause).
- Calculates required votes for 5-player team (majority)
- Calculates required votes for 4-player team (3 of 4)
- Calculates required votes for 3-player team
- Handles minimum threshold (always at least 1)
- Returns correct threshold for full team

#### `tests/FiveStack.Tests/Services/TimeoutSystemTests.cs` — Timeout & Pause Logic (6 Theory tests, ~18 test cases)
Tests timeout configuration parsing and pause/timeout authorization logic using xUnit `[Theory]` with `[InlineData]`.

- **`TimeoutSettingStringToEnum` (multiple inline cases):**
  - Parses "CoachAndCaptains" string to enum
  - Parses "CoachAndPlayers" string to enum
  - Parses "Coach" string to enum
  - Parses "Admin" string to enum
  - Returns default for unknown string

- **`CanPause` decision table (11 inline cases):**
  - Coach setting: coach can pause, captain cannot, regular player cannot
  - CoachAndCaptains setting: coach can pause, captain can pause, regular player cannot
  - CoachAndPlayers setting: coach can pause, captain can pause, regular player can pause
  - Admin setting: no one on team can pause (admin-only)
  - Handles null coach ID correctly

- **`CanCallTacticalTimeout` decision table (7 inline cases):**
  - Only coach can call tactical timeout in Coach mode
  - Coach and captains can call in CoachAndCaptains mode
  - Anyone can call in CoachAndPlayers mode
  - No one can call in Admin mode
  - Validates remaining timeout count > 0

#### `tests/FiveStack.Tests/Services/GameBackUpRoundsTests.cs` — Backup & Restore (12 tests)
Tests round backup file management and restore logic.

- **`GetSafeMatchPrefix` (2 tests):**
  - Generates safe file prefix from match UUID
  - Strips invalid characters from prefix

- **File naming (3 tests):**
  - Pads round numbers correctly in backup filenames (e.g., round 3 → `003`)
  - Pads round numbers in restore filenames
  - Handles double-digit round numbers

- **`CheckForBackupRestore` (4 tests):**
  - Identifies when restore is needed (current round > expected round)
  - Skips restore when rounds match
  - Selects correct backup file for target round
  - Handles missing backup files gracefully

- **State & data (3 tests):**
  - `IsResettingRound` returns correct state flag
  - `GetMemberFromLineup` finds correct player by Steam ID
  - Restore event data contains correct round and file path

#### `tests/FiveStack.Tests/Services/CaptainSystemTests.cs` — Captain Management (15 tests)
Tests the captain assignment and validation system.

- **Captain dictionary (3 tests):**
  - Initializes empty captain map
  - Adds captain for team
  - Replaces existing captain for team

- **Team validation (3 tests):**
  - Validates captain belongs to correct team
  - Rejects captain from wrong team
  - Handles player not in any team

- **`IsCaptain` decision flow (4 tests):**
  - Returns true for assigned captain
  - Returns false for non-captain player
  - Returns false for player on different team
  - Handles null player reference

- **`RemoveCaptain` guards (3 tests):**
  - Removes captain successfully
  - No-op when removing non-existent captain
  - No-op when removing from team with no captain

- **Event data (2 tests):**
  - Captain assignment event contains correct team and player
  - Captain removal event contains correct team

---

## Testing Approach

The game server uses CounterStrikeSharp, which has static APIs (`Server.GameDirectory`, `Server.NextFrame`) and sealed types (`CCSPlayerController`) that cannot be mocked with Moq. Tests are written by:

1. **Extracting pure logic** — Decision functions (CanPause, CanCallTacticalTimeout, vote thresholds) are tested independently from their game-server integration
2. **Using `TestDataFactory`** — Creates properly-shaped entity objects without needing the game runtime
3. **xUnit `[Theory]` + `[InlineData]`** — Parameterized tests cover decision tables exhaustively

---

## CI/CD

GitHub Actions workflow (`.github/workflows/test.yml`) runs on push/PR to `main` and `develop`:
- Single `unit-tests` job: checkout → setup .NET 8.0 → dotnet test with XPlat Code Coverage → upload coverage artifact
