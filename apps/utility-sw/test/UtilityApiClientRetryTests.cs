using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using UtilityPractice;
using Xunit;

using eIngestOutcome = UtilityPractice.UtilityApiClient.eIngestOutcome;

public class UtilityApiClientRetryTests : IDisposable
{
    private const int RetrySeconds = 10;
    private const string Mirage = "de_mirage";
    private const string Inferno = "de_inferno";
    private const string Workshop = "3070284539";

    private readonly FakePanel _panel = new();
    private readonly string _configDirectory = Directory.CreateTempSubdirectory().FullName;
    private readonly UtilityApiClient _api;
    private readonly List<(LineupRecord Save, UtilityApiClient.IngestResult Result)> _retried = new();

    public UtilityApiClientRetryTests()
    {
        File.WriteAllText(
            Path.Join(_configDirectory, "utility-practice.json"),
            $$"""{ "utility_url": "{{_panel.Url}}", "server_id": "{{FakePanel.ServerId}}", "server_api_password": "{{FakePanel.Password}}" }"""
        );

        UtilityConfig config = new(NullLogger<UtilityConfig>.Instance);
        config.Load(_configDirectory);

        _api = new UtilityApiClient(config, NullLogger<UtilityApiClient>.Instance) { Map = Mirage };
        _api.SaveRetried += (record, result) => _retried.Add((record, result));
    }

    public void Dispose()
    {
        _panel.Dispose();
        Directory.Delete(_configDirectory, true);
    }

    private static LineupRecord Save(string name = "A site smoke", string map = Mirage)
    {
        return new LineupRecord
        {
            client_id = Guid.NewGuid().ToString(),
            name = name,
            map = map,
            author_steam_id = FakePanel.Author,
            flight_time = 1.5f,
        };
    }

    private static UtilityPracticeResultPayload Result(float x = 1f, Guid? session = null)
    {
        return UtilityPracticeResultPayload.For(
            null,
            session ?? Guid.Parse(FakePanel.SessionId),
            Guid.NewGuid().ToString(),
            ulong.Parse(FakePanel.Author),
            new Vec3(x, 2f, 3f),
            null
        );
    }

    private List<string> WatchFailedDeletes()
    {
        List<string> failed = new();
        _api.WithdrawnDeleteFailed += (_, name) => failed.Add(name);

        return failed;
    }

    private async Task Seconds(int count)
    {
        for (int second = 0; second < count; second++)
        {
            await _api.Second();
        }
    }

    [Fact]
    public async Task AResponseLostAfterThePanelSavedItStaysOneLineup()
    {
        _panel.ProcessThenDrop();

        UtilityApiClient.IngestResult first = await _api.Ingest(Save());

        Assert.Equal(eIngestOutcome.Held, first.Outcome);
        Assert.Equal(1, _api.QueuedCount);

        await Seconds(RetrySeconds);

        Assert.Equal(2, _panel.Ingests);
        Assert.Equal(1, _panel.Rows);
        Assert.Equal(0, _api.QueuedCount);
    }

    [Fact]
    public async Task ASaveThatNeverArrivedIsSentAgainWithoutAnotherSaveComingAlong()
    {
        _panel.Cut();

        await _api.Ingest(Save());

        Assert.Equal(0, _panel.Rows);

        await Seconds(RetrySeconds);

        Assert.Equal(1, _panel.Rows);
        Assert.Equal(0, _api.QueuedCount);
    }

    [Fact]
    public async Task AHeldSaveWaitsTheFullIntervalBeforeTryingAgain()
    {
        _panel.Cut();

        await _api.Ingest(Save());
        await Seconds(RetrySeconds - 1);

        Assert.Equal(1, _panel.Ingests);
        Assert.Equal(1, _api.QueuedCount);
    }

    [Fact]
    public async Task APanelThatStaysDownIsAskedOncePerInterval()
    {
        _panel.GoDown();

        await _api.Ingest(Save());
        await Seconds(RetrySeconds * 2);

        Assert.Equal(3, _panel.Ingests);
        Assert.Equal(1, _api.QueuedCount);
    }

    [Fact]
    public async Task NothingHeldSendsNothing()
    {
        await Seconds(RetrySeconds * 2);

        Assert.Equal(0, _panel.Ingests);
    }

    [Fact]
    public async Task ARetriedSaveHandsBackThePanelId()
    {
        _panel.Cut();

        LineupRecord save = Save();

        await _api.Ingest(save);
        await Seconds(RetrySeconds);

        (LineupRecord record, UtilityApiClient.IngestResult result) = Assert.Single(_retried);
        Assert.Same(save, record);
        Assert.Equal(eIngestOutcome.Delivered, result.Outcome);
        Assert.Equal(_panel.IdNamed(save.name), result.Id);
    }

    [Fact]
    public async Task AnInvalidSaveIsNeverQueued()
    {
        LineupRecord stranger = Save();
        stranger.author_steam_id = "76561198999999999";

        UtilityApiClient.IngestResult result = await _api.Ingest(stranger);

        Assert.Equal(eIngestOutcome.Refused, result.Outcome);
        Assert.Equal(0, _api.QueuedCount);

        await Seconds(RetrySeconds);

        Assert.Equal(1, _panel.Ingests);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task ACredentialOrCapacityAnswerIsHeldForLater(int status)
    {
        _panel.Answer(status);

        UtilityApiClient.IngestResult result = await _api.Ingest(Save());

        Assert.Equal(eIngestOutcome.Held, result.Outcome);
        Assert.Equal(1, _api.QueuedCount);
    }

    [Fact]
    public async Task ASaveRefusedOnARetryDoesNotBlockTheOnesBehindIt()
    {
        _panel.Cut();
        _panel.Cut();
        _panel.Answer(400);

        LineupRecord refused = Save("refused");
        LineupRecord behind = Save("behind");

        await _api.Ingest(refused);
        await _api.Ingest(behind);
        await Seconds(RetrySeconds);

        Assert.Equal(0, _api.QueuedCount);
        Assert.Equal(2, _retried.Count);
        Assert.Same(refused, _retried[0].Save);
        Assert.Equal(eIngestOutcome.Refused, _retried[0].Result.Outcome);
        Assert.Same(behind, _retried[1].Save);
        Assert.Equal(_panel.IdNamed(behind.name), _retried[1].Result.Id);
    }

    [Fact]
    public async Task AnAnsweredSaveWithNoIdIsNotSentAgain()
    {
        _panel.Answer(200, "{}");

        UtilityApiClient.IngestResult result = await _api.Ingest(Save());

        Assert.Equal(eIngestOutcome.Delivered, result.Outcome);
        Assert.Null(result.Id);
        Assert.Equal(0, _api.QueuedCount);
    }

    [Fact]
    public async Task AnAnsweredSaveWithAnUnreadableBodyIsNotSentAgain()
    {
        _panel.Answer(200, "not json");

        UtilityApiClient.IngestResult result = await _api.Ingest(Save());

        Assert.Equal(eIngestOutcome.Delivered, result.Outcome);
        Assert.Equal(0, _api.QueuedCount);
    }

    [Fact]
    public async Task ASaveThatCannotBeSerializedIsDroppedRatherThanQueued()
    {
        LineupRecord broken = Save();
        broken.detonation_position = new Vec3(float.NaN, 0f, 0f);

        UtilityApiClient.IngestResult result = await _api.Ingest(broken);

        Assert.Equal(eIngestOutcome.Refused, result.Outcome);
        Assert.Equal(0, _api.QueuedCount);
        Assert.Equal(0, _panel.Ingests);
    }

    [Fact]
    public async Task AHeadThatNeverGoesThroughIsGivenUpOnSoTheNextCanTry()
    {
        _panel.GoDown();

        LineupRecord stuck = Save("stuck");

        await _api.Ingest(stuck);
        await Seconds(RetrySeconds * (UtilityApiClient.MaxAttempts - 1));

        LineupRecord behind = Save("behind");
        await _api.Ingest(behind);

        Assert.Empty(_retried);

        _panel.ComeBackFor(behind.name);

        await Seconds(RetrySeconds);

        Assert.Equal(2, _retried.Count);
        Assert.Same(stuck, _retried[0].Save);
        Assert.Equal(eIngestOutcome.Refused, _retried[0].Result.Outcome);
        Assert.Same(behind, _retried[1].Save);
        Assert.Equal(eIngestOutcome.Delivered, _retried[1].Result.Outcome);
        Assert.Equal(0, _api.QueuedCount);
    }

    [Fact]
    public async Task ThePlayerAskingCanDeleteTheirOwnLineup()
    {
        LineupRecord save = Save();

        UtilityApiClient.IngestResult saved = await _api.Ingest(save);

        Assert.True(await _api.Delete(saved.Id!, FakePanel.Author));
        Assert.Equal(0, _panel.Rows);
    }

    [Fact]
    public async Task ADeleteByAnotherPlayerIsRefused()
    {
        UtilityApiClient.IngestResult saved = await _api.Ingest(Save());

        Assert.False(await _api.Delete(saved.Id!, FakePanel.TeamMate));
        Assert.Equal(1, _panel.Rows);
    }

    [Fact]
    public async Task ADeleteByAPlayerNotInTheLineupIsRefused()
    {
        UtilityApiClient.IngestResult saved = await _api.Ingest(Save());

        Assert.False(await _api.Delete(saved.Id!, "76561198999999999"));
        Assert.Equal(1, _panel.Rows);
    }

    [Fact]
    public async Task AFailedDeleteOfAWithdrawnSaveIsReported()
    {
        _panel.ProcessThenDrop();
        _panel.RefuseDeletes();

        LineupRecord deleted = Save("kept by the panel");
        List<(string SteamId, string Name)> failed = new();
        _api.WithdrawnDeleteFailed += (steamId, name) => failed.Add((steamId, name));

        await _api.Ingest(deleted);

        _api.Withdraw(deleted.client_id);

        await Seconds(RetrySeconds);

        Assert.Equal((FakePanel.Author, "kept by the panel"), Assert.Single(failed));
        Assert.Equal(1, _panel.Rows);
    }

    [Fact]
    public async Task AHeldSaveIsSentAsItWasWhenItWasSaved()
    {
        _panel.Cut();

        LineupRecord thrown = Save("first name");

        await _api.Ingest(thrown);

        thrown.name = "second name";

        await Seconds(RetrySeconds);

        Assert.NotNull(_panel.IdNamed("first name"));
        Assert.Equal("first name", Assert.Single(_retried).Result.Name);
    }

    [Fact]
    public async Task ASaveEvictedFromAFullQueueIsReported()
    {
        const int full = 64;

        _panel.GoDown();

        for (int save = 0; save < full; save++)
        {
            await _api.Ingest(Save($"save {save}"));
        }

        Assert.Empty(_retried);

        await _api.Ingest(Save("newest"));

        (LineupRecord record, UtilityApiClient.IngestResult result) = Assert.Single(_retried);
        Assert.Equal("save 0", record.name);
        Assert.Equal("save 0", result.Name);
        Assert.Equal(eIngestOutcome.Refused, result.Outcome);
    }

    [Fact]
    public async Task ASaveThePanelHasMovedOnFromIsRefusedNotFiled()
    {
        _panel.MoveTo(Inferno);

        UtilityApiClient.IngestResult result = await _api.Ingest(Save());

        Assert.Equal(eIngestOutcome.Refused, result.Outcome);
        Assert.Equal(0, _panel.Rows);
        Assert.Equal(0, _api.QueuedCount);
    }

    [Fact]
    public async Task AHeldSaveThePanelHasMovedOnFromIsRefusedOnItsRetry()
    {
        _panel.Cut();

        await _api.Ingest(Save());

        _panel.MoveTo(Inferno);

        await Seconds(RetrySeconds);

        Assert.Equal(eIngestOutcome.Refused, Assert.Single(_retried).Result.Outcome);
        Assert.Equal(0, _panel.Rows);
        Assert.Equal(0, _api.QueuedCount);
    }

    [Fact]
    public async Task ASaveNamesTheMapItWasMadeOnOnEveryTry()
    {
        _panel.Cut();

        LineupRecord thrown = Save();

        await _api.Ingest(thrown, Workshop);

        thrown.map = Inferno;

        await Seconds(RetrySeconds);

        Assert.Equal(new[] { Mirage, Mirage }, _panel.SentMaps);
        Assert.Equal(new[] { Workshop, Workshop }, _panel.SentWorkshopIds);
    }

    [Fact]
    public async Task AWorkshopMapIsMatchedByItsId()
    {
        _panel.MoveTo("seeded workshop name", Workshop);

        UtilityApiClient.IngestResult result = await _api.Ingest(Save(), Workshop);

        Assert.Equal(eIngestOutcome.Delivered, result.Outcome);
        Assert.Equal(1, _panel.Rows);
    }

    [Fact]
    public async Task AStockMapSendsNoWorkshopId()
    {
        await _api.Ingest(Save(), "");

        Assert.Equal(Mirage, Assert.Single(_panel.SentMaps));
        Assert.Equal("", Assert.Single(_panel.SentWorkshopIds));
    }

    [Fact]
    public async Task APanelWithoutTheCheckStillFilesTheSave()
    {
        _panel.IgnoreMapChecks();
        _panel.MoveTo(Inferno);

        UtilityApiClient.IngestResult result = await _api.Ingest(Save());

        Assert.Equal(eIngestOutcome.Delivered, result.Outcome);
    }

    [Fact]
    public async Task AResultForALineupOnAnotherMapIsDropped()
    {
        UtilityPracticeResultPayload thrownOnMirage = Result();

        _panel.PlaceLineup(thrownOnMirage.utility_lineup_id!, Mirage);
        _panel.MoveTo(Inferno);

        Assert.Null(await _api.PracticeResult(thrownOnMirage));
        Assert.Equal(0, _panel.Counted);
        Assert.Equal(0, _api.QueuedCount);
    }

    [Fact]
    public async Task AWithdrawnSaveGivenUpOnIsReported()
    {
        List<string> failed = WatchFailedDeletes();

        _panel.GoDown();

        LineupRecord deleted = Save("given up");

        await _api.Ingest(deleted);

        _api.Withdraw(deleted.client_id);

        await Seconds(RetrySeconds * UtilityApiClient.MaxAttempts);

        Assert.Equal("given up", Assert.Single(failed));
        Assert.Equal(0, _api.QueuedCount);
    }

    [Fact]
    public async Task AWithdrawnSaveEvictedFromAFullQueueIsReported()
    {
        List<string> failed = WatchFailedDeletes();

        _panel.GoDown();

        LineupRecord deleted = Save("evicted");

        await _api.Ingest(deleted);

        _api.Withdraw(deleted.client_id);

        for (int save = 0; save < 64; save++)
        {
            await _api.Ingest(Save($"save {save}"));
        }

        Assert.Equal("evicted", Assert.Single(failed));
        Assert.Empty(_retried);
    }

    [Fact]
    public async Task AWithdrawnSaveThePanelTurnedDownHasNothingToReport()
    {
        List<string> failed = WatchFailedDeletes();

        _panel.Cut();
        _panel.Answer(400);

        LineupRecord deleted = Save();

        await _api.Ingest(deleted);

        _api.Withdraw(deleted.client_id);

        await Seconds(RetrySeconds);

        Assert.Empty(failed);
        Assert.Equal(0, _api.QueuedCount);
    }

    [Fact]
    public async Task AWithdrawnSaveHeldAfterItsFirstSendIsNotReportedEarly()
    {
        List<string> failed = WatchFailedDeletes();

        TaskCompletionSource release = new();
        _panel.Stall(release, drop: true);

        LineupRecord deleted = Save();
        Task<UtilityApiClient.IngestResult> sending = _api.Ingest(deleted);

        await _panel.UntilIngests(1);

        _api.Withdraw(deleted.client_id);
        release.SetResult();

        await sending;

        Assert.Empty(failed);
    }

    [Fact]
    public async Task OneThrowSavedTwiceIsTwoLineups()
    {
        LineupRecord thrown = Save();

        await _api.Ingest(thrown);
        await _api.Ingest(thrown);

        Assert.Equal(2, _panel.Rows);
        Assert.Equal(2, _panel.SentIds.Distinct().Count());
    }

    [Fact]
    public async Task OneSaveRetriedSendsTheSameIdEveryTime()
    {
        _panel.GoDown();

        await _api.Ingest(Save());
        await Seconds(RetrySeconds * 2);

        Assert.Equal(3, _panel.SentIds.Count);
        Assert.Single(_panel.SentIds.Distinct());
    }

    [Fact]
    public async Task TwoHeldSavesOfOneThrowKeepTheirOwnIds()
    {
        _panel.Cut();
        _panel.Cut();

        LineupRecord thrown = Save();

        await _api.Ingest(thrown);
        await _api.Ingest(thrown);
        await Seconds(RetrySeconds);

        Assert.Equal(2, _panel.Rows);
        Assert.Equal(0, _api.QueuedCount);
    }

    [Fact]
    public async Task ADeletedSaveWhoseResponseWasLostLeavesNoRow()
    {
        _panel.ProcessThenDrop();

        LineupRecord deleted = Save();

        await _api.Ingest(deleted);

        Assert.Equal(1, _panel.Rows);

        _api.Withdraw(deleted.client_id);

        await Seconds(RetrySeconds);

        Assert.Equal(0, _panel.Rows);
        Assert.Equal(0, _api.QueuedCount);
        Assert.Empty(_retried);
    }

    [Fact]
    public async Task ADeletedSaveThatNeverArrivedLeavesNoRow()
    {
        _panel.Cut();

        LineupRecord deleted = Save();

        await _api.Ingest(deleted);

        _api.Withdraw(deleted.client_id);

        await Seconds(RetrySeconds);

        Assert.Equal(0, _panel.Rows);
        Assert.Equal(0, _api.QueuedCount);
        Assert.Empty(_retried);
    }

    [Fact]
    public async Task ASaveWithdrawnBehindAnotherLeavesOnlyTheOther()
    {
        _panel.Cut();
        _panel.Cut();

        LineupRecord head = Save("head");
        LineupRecord deleted = Save("deleted");

        await _api.Ingest(head);
        await _api.Ingest(deleted);

        _api.Withdraw(deleted.client_id);

        await Seconds(RetrySeconds);

        Assert.Equal(1, _panel.Rows);
        Assert.Equal(0, _api.QueuedCount);
        Assert.Same(head, Assert.Single(_retried).Save);
    }

    [Fact]
    public async Task ASaveWithdrawnWhileItIsOnItsWayIsDeletedFromThePanel()
    {
        _panel.Cut();

        LineupRecord deleted = Save();

        await _api.Ingest(deleted);

        TaskCompletionSource release = new();
        _panel.Stall(release);

        Task retry = Seconds(RetrySeconds);

        await _panel.UntilIngests(2);

        _api.Withdraw(deleted.client_id);
        release.SetResult();

        await retry;

        Assert.Equal(0, _panel.Rows);
        Assert.Equal(0, _api.QueuedCount);
        Assert.Empty(_retried);
    }

    [Fact]
    public async Task AnEvictedHeadDoesNotTakeTheNextSaveWithIt()
    {
        const int full = 64;

        _panel.GoDown();

        List<LineupRecord> saves = Enumerable.Range(0, full).Select(i => Save($"save {i}")).ToList();

        foreach (LineupRecord save in saves)
        {
            await _api.Ingest(save);
        }

        TaskCompletionSource release = new();
        _panel.Stall(release);

        Task retry = Seconds(RetrySeconds);

        await _panel.UntilIngests(full + 1);

        await _api.Ingest(Save("newest"));

        release.SetResult();

        await retry;

        Assert.Equal(full, _api.QueuedCount);
    }

    [Fact]
    public async Task ASaveThatLandedStillReachesThePanelAfterTheMapMoved()
    {
        _panel.ProcessThenDrop();

        LineupRecord save = Save();

        await _api.Ingest(save);

        _panel.MoveTo(Inferno);
        _api.Map = Inferno;

        await Seconds(RetrySeconds);

        Assert.Equal(2, _panel.Ingests);
        Assert.Equal(1, _panel.Rows);
        Assert.Equal(_panel.IdNamed(save.name), Assert.Single(_retried).Result.Id);
    }

    [Fact]
    public async Task ASaveThatNeverLandedIsLeftForThePanelToRefuse()
    {
        _panel.Cut();

        await _api.Ingest(Save());

        _panel.MoveTo(Inferno);
        _api.Map = Inferno;

        await Seconds(RetrySeconds);

        Assert.Equal(2, _panel.Ingests);
        Assert.Equal(0, _panel.Rows);
        Assert.Equal(eIngestOutcome.Refused, Assert.Single(_retried).Result.Outcome);
    }

    [Fact]
    public async Task ASaveNeverSentIsRefusedHereOnceTheMapMoved()
    {
        _api.Map = "";

        await _api.Ingest(Save());

        _api.Map = Inferno;

        await Seconds(RetrySeconds);

        Assert.Equal(0, _panel.Ingests);
        Assert.Equal(eIngestOutcome.Refused, Assert.Single(_retried).Result.Outcome);
    }

    [Fact]
    public async Task AWithdrawnSaveThatLandedIsDeletedAfterTheMapMoved()
    {
        List<string> failed = WatchFailedDeletes();

        _panel.ProcessThenDrop();

        LineupRecord deleted = Save();

        await _api.Ingest(deleted);

        _api.Withdraw(deleted.client_id);
        _panel.MoveTo(Inferno);
        _api.Map = Inferno;

        await Seconds(RetrySeconds);

        Assert.Equal(0, _panel.Rows);
        Assert.Empty(failed);
    }

    [Fact]
    public async Task AWithdrawnSaveThatNeverLandedHasNothingToReport()
    {
        List<string> failed = WatchFailedDeletes();

        _panel.Cut();

        LineupRecord deleted = Save();

        await _api.Ingest(deleted);

        _api.Withdraw(deleted.client_id);
        _panel.MoveTo(Inferno);

        await Seconds(RetrySeconds);

        Assert.Empty(failed);
        Assert.Equal(0, _api.QueuedCount);
    }

    [Fact]
    public async Task AWithdrawnSaveNeverSentHasNothingToReport()
    {
        List<string> failed = WatchFailedDeletes();

        _api.Map = "";

        LineupRecord deleted = Save();

        await _api.Ingest(deleted);

        _api.Withdraw(deleted.client_id);
        _api.Map = Inferno;

        await Seconds(RetrySeconds);

        Assert.Empty(failed);
        Assert.Equal(0, _api.QueuedCount);
    }

    [Fact]
    public async Task AWithdrawnSaveEvictedBeforeItWasSentHasNothingToReport()
    {
        List<string> failed = WatchFailedDeletes();

        _api.Map = "";

        LineupRecord deleted = Save();

        await _api.Ingest(deleted);

        _api.Withdraw(deleted.client_id);

        for (int save = 0; save < 64; save++)
        {
            await _api.Ingest(Save($"save {save}"));
        }

        Assert.Empty(failed);
    }

    [Fact]
    public async Task AnOfficialMapStillSendsTheWorkshopKeyEmpty()
    {
        await _api.Session(Mirage, "", new SteamRelayReport(false, null));

        string query = Assert.Single(_panel.SessionQueries);

        Assert.Matches(@"(^|&)workshop_id=(&|$)", query);
        Assert.Matches(@"(^|&)map=de_mirage(&|$)", query);
    }

    [Fact]
    public async Task AnUnknownWorkshopIdIsSentAsAnOfficialMapToo()
    {
        await _api.Session(Mirage, null, new SteamRelayReport(false, null));

        Assert.Matches(@"(^|&)workshop_id=(&|$)", Assert.Single(_panel.SessionQueries));
    }

    [Fact]
    public async Task AWorkshopMapSendsItsIdWithItsSession()
    {
        await _api.Session("id-named", Workshop, new SteamRelayReport(false, null));

        Assert.Matches($"(^|&)workshop_id={Workshop}(&|$)", Assert.Single(_panel.SessionQueries));
    }

    [Fact]
    public async Task AFirstSendFromAnotherMapIsRefusedToo()
    {
        _api.Map = "de_inferno";

        UtilityApiClient.IngestResult result = await _api.Ingest(Save());

        Assert.Equal(eIngestOutcome.Refused, result.Outcome);
        Assert.Equal(0, _panel.Ingests);
    }

    [Fact]
    public async Task AnUnknownServerMapHoldsASaveRatherThanSendingIt()
    {
        _api.Map = "";

        UtilityApiClient.IngestResult result = await _api.Ingest(Save());

        Assert.Equal(eIngestOutcome.Held, result.Outcome);
        Assert.Equal(1, _api.QueuedCount);

        await Seconds(RetrySeconds);

        Assert.Equal(0, _panel.Ingests);
        Assert.Equal(1, _api.QueuedCount);

        _api.Map = Mirage;

        await Seconds(RetrySeconds);

        Assert.Equal(1, _panel.Rows);
        Assert.Equal(0, _api.QueuedCount);
    }

    [Fact]
    public async Task ASaveThatNamesNoMapIsNotSent()
    {
        UtilityApiClient.IngestResult result = await _api.Ingest(Save(map: ""));

        Assert.Equal(eIngestOutcome.Refused, result.Outcome);
        Assert.Equal(0, _panel.Ingests);
        Assert.Equal(0, _api.QueuedCount);
    }

    [Fact]
    public async Task ASaveDeletedDuringItsFirstSendIsDeletedFromThePanel()
    {
        TaskCompletionSource release = new();
        _panel.Stall(release);

        LineupRecord deleted = Save();
        Task<UtilityApiClient.IngestResult> sending = _api.Ingest(deleted);

        await _panel.UntilIngests(1);

        _api.Withdraw(deleted.client_id);
        release.SetResult();

        UtilityApiClient.IngestResult result = await sending;

        Assert.Equal(eIngestOutcome.Withdrawn, result.Outcome);
        Assert.Null(result.Id);
        Assert.Equal(0, _panel.Rows);
    }

    [Fact]
    public async Task ASaveDeletedDuringAFirstSendThatLandedButLostItsAnswerLeavesNoRow()
    {
        TaskCompletionSource release = new();
        _panel.Stall(release, drop: true);

        LineupRecord deleted = Save();
        Task<UtilityApiClient.IngestResult> sending = _api.Ingest(deleted);

        await _panel.UntilIngests(1);

        _api.Withdraw(deleted.client_id);
        release.SetResult();

        Assert.Equal(eIngestOutcome.Withdrawn, (await sending).Outcome);
        Assert.Equal(1, _panel.Rows);

        await Seconds(RetrySeconds);

        Assert.Equal(0, _panel.Rows);
        Assert.Equal(0, _api.QueuedCount);
        Assert.Empty(_retried);
    }

    [Fact]
    public async Task AResultWhoseResponseWasLostCountsOnce()
    {
        _panel.ProcessThenDrop();

        Assert.Null(await _api.PracticeResult(Result()));
        Assert.Equal(1, _api.QueuedCount);

        await Seconds(RetrySeconds);

        Assert.Equal(2, _panel.Results);
        Assert.Equal(1, _panel.Counted);
        Assert.Equal(0, _api.QueuedCount);
    }

    [Fact]
    public async Task AResultForAStaleSessionIsDroppedRatherThanHeld()
    {
        Assert.Null(await _api.PracticeResult(Result(session: Guid.NewGuid())));
        Assert.Equal(0, _api.QueuedCount);
        Assert.Equal(0, _panel.Counted);
    }

    [Fact]
    public async Task AResultGoneStaleOnARetryDoesNotBlockTheOnesBehindIt()
    {
        _panel.Cut();
        _panel.Cut();
        _panel.Answer(409);

        await _api.PracticeResult(Result(1f));
        await _api.PracticeResult(Result(2f));
        await Seconds(RetrySeconds);

        Assert.Equal(1, _panel.Counted);
        Assert.Equal(0, _api.QueuedCount);
    }

    [Fact]
    public async Task ARateLimitedResultIsHeld()
    {
        _panel.Answer(429);

        Assert.Null(await _api.PracticeResult(Result()));
        Assert.Equal(1, _api.QueuedCount);
    }

    [Fact]
    public async Task AResultThatCannotBeSerializedIsDroppedRatherThanQueued()
    {
        Assert.Null(await _api.PracticeResult(Result(float.NaN)));

        Assert.Equal(0, _api.QueuedCount);
        Assert.Equal(0, _panel.Results);
    }

    [Fact]
    public async Task AResultThatNeverGoesThroughIsGivenUpOn()
    {
        _panel.GoDown();

        await _api.PracticeResult(Result());
        await Seconds(RetrySeconds * UtilityApiClient.MaxAttempts);

        Assert.Equal(0, _api.QueuedCount);
    }

    // Answers as the panel does, not as these tests would like: the guard and the
    // checks below are the panel's own, read from utility.controller.ts,
    // utility-plugin-key.guard.ts and utility-lineups.service.ts. A scripted step
    // only applies to a request that passed them.
    private sealed class FakePanel : IDisposable
    {
        public const string ServerId = "7a1f3c52-0d4e-4b6a-9c8e-2f5d6b7a8c90";
        public const string Password = "secret";
        public const string Author = "76561198000000001";
        public const string TeamMate = "76561198000000002";
        public const string SessionId = "1b2c3d4e-5f60-4718-8a9b-0c1d2e3f4a5b";

        private const int MinFlightMs = 50;
        private const int MaxFlightMs = 15000;

        private static readonly Regex Uuid = new(
            "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
            RegexOptions.IgnoreCase
        );

        private static readonly HashSet<string> Roster = new() { Author, TeamMate };

        private enum Fault
        {
            None,
            Cut,
            ProcessThenDrop,
        }

        private sealed record Step(
            Fault Fault = Fault.None,
            int? Status = null,
            string? Body = null,
            Task? Wait = null
        );

        private sealed record Request(string Method, string Path, string Query, string Password, string Body);

        private sealed record Row(string Id, string Author, string? ClientId, string Name);

        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly ConcurrentQueue<Step> _steps = new();
        private readonly List<Row> _rows = new();
        private readonly HashSet<string> _counted = new();
        private readonly List<string> _sentIds = new();
        private readonly List<string> _sentMaps = new();
        private readonly List<string> _sessionQueries = new();
        private readonly List<string> _sentWorkshopIds = new();
        private readonly object _lock = new();
        private readonly Dictionary<string, string> _lineupMaps = new();
        private string _map = "de_mirage";
        private string? _workshopId;
        private bool _checksMaps = true;
        private bool _down;
        private bool _refusingDeletes;
        private string? _backFor;
        private int _ingests;
        private int _results;

        public FakePanel()
        {
            _listener.Start();

            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

            _ = Task.Run(Serve);
        }

        public string Url { get; }

        public int Ingests => Volatile.Read(ref _ingests);

        public int Results => Volatile.Read(ref _results);

        public int Rows
        {
            get
            {
                lock (_lock)
                {
                    return _rows.Count;
                }
            }
        }

        public int Counted
        {
            get
            {
                lock (_lock)
                {
                    return _counted.Count;
                }
            }
        }

        public List<string> SentIds
        {
            get
            {
                lock (_lock)
                {
                    return _sentIds.ToList();
                }
            }
        }

        public List<string> SessionQueries
        {
            get
            {
                lock (_lock)
                {
                    return _sessionQueries.ToList();
                }
            }
        }

        public List<string> SentMaps
        {
            get
            {
                lock (_lock)
                {
                    return _sentMaps.ToList();
                }
            }
        }

        public List<string> SentWorkshopIds
        {
            get
            {
                lock (_lock)
                {
                    return _sentWorkshopIds.ToList();
                }
            }
        }

        public string IdNamed(string name)
        {
            lock (_lock)
            {
                return _rows.Single(row => row.Name == name).Id;
            }
        }

        public void Cut()
        {
            _steps.Enqueue(new Step(Fault.Cut));
        }

        public void ProcessThenDrop()
        {
            _steps.Enqueue(new Step(Fault.ProcessThenDrop));
        }

        public void Answer(int status, string body = "{\"message\":\"no\"}")
        {
            _steps.Enqueue(new Step(Status: status, Body: body));
        }

        public void Stall(TaskCompletionSource release, bool drop = false)
        {
            _steps.Enqueue(new Step(drop ? Fault.ProcessThenDrop : Fault.None, Wait: release.Task));
        }

        public void GoDown()
        {
            _down = true;
        }

        public void ComeBackFor(string name)
        {
            _backFor = $"\"name\":\"{name}\"";
        }

        public void PlaceLineup(string id, string map)
        {
            lock (_lock)
            {
                _lineupMaps[id] = map;
            }
        }

        public void MoveTo(string map, string? workshopId = null)
        {
            lock (_lock)
            {
                _map = map;
                _workshopId = workshopId;
            }
        }

        public void IgnoreMapChecks()
        {
            _checksMaps = false;
        }

        public void RefuseDeletes()
        {
            _refusingDeletes = true;
        }

        public async Task UntilIngests(int count)
        {
            DateTime giveUp = DateTime.UtcNow.AddSeconds(5);

            while (Ingests < count)
            {
                Assert.True(DateTime.UtcNow < giveUp, "the panel never saw the send");
                await Task.Delay(5);
            }
        }

        public void Dispose()
        {
            _listener.Stop();
        }

        private static bool IsIngest(Request request)
        {
            return request.Method == "POST" && request.Path == "/utility/ingest";
        }

        private static bool IsResult(Request request)
        {
            return request.Method == "POST" && request.Path == "/utility/practice-result";
        }

        private static bool IsSession(Request request)
        {
            return request.Method == "GET" && request.Path == "/utility/session";
        }

        private static bool IsDelete(Request request)
        {
            return request.Method == "DELETE" && request.Path.Split('/').Length == 3;
        }

        private async Task Serve()
        {
            while (true)
            {
                TcpClient connection;

                try
                {
                    connection = await _listener.AcceptTcpClientAsync();
                }
                catch (Exception error) when (error is SocketException or ObjectDisposedException)
                {
                    return;
                }

                _ = Task.Run(() => Handle(connection));
            }
        }

        private async Task Handle(TcpClient connection)
        {
            using (connection)
            {
                Request request = await Read(connection.GetStream());

                if (IsIngest(request))
                {
                    Interlocked.Increment(ref _ingests);

                    lock (_lock)
                    {
                        _sentIds.Add(Field(request.Body, "client_id") ?? "");
                        _sentMaps.Add(Field(request.Body, "map") ?? "");
                        _sentWorkshopIds.Add(Field(request.Body, "workshop_id") ?? "");
                    }
                }
                else if (IsResult(request))
                {
                    Interlocked.Increment(ref _results);
                }

                if (Refusal(request) is (int status, string reason))
                {
                    Write(connection, status, reason);
                    return;
                }

                Step step = Next(request);

                if (step.Wait != null)
                {
                    await step.Wait;
                }

                if (step.Fault == Fault.Cut)
                {
                    Reset(connection);
                    return;
                }

                if (step.Status != null)
                {
                    Write(connection, step.Status.Value, step.Body!);
                    return;
                }

                (int processed, string answer) = Process(request);

                if (step.Fault == Fault.ProcessThenDrop)
                {
                    Reset(connection);
                    return;
                }

                Write(connection, processed, answer);
            }
        }

        private (int Status, string Body)? Refusal(Request request)
        {
            string serverId = Query(request, "server_id") ?? Field(request.Body, "server_id") ?? "";

            if (request.Password != Password || !Uuid.IsMatch(serverId))
            {
                return Refused(403);
            }

            if (IsIngest(request))
            {
                return RefusedSave(request.Body);
            }

            if (IsResult(request))
            {
                return RefusedResult(request.Body);
            }

            if (IsSession(request))
            {
                lock (_lock)
                {
                    _sessionQueries.Add(request.Query);
                }

                return null;
            }

            if (IsDelete(request))
            {
                return Roster.Contains(Query(request, "steam_id") ?? "") ? null : Refused(403);
            }

            return Refused(404);
        }

        private static (int, string) Refused(int status)
        {
            return (status, "{\"message\":\"refused\"}");
        }

        // A plugin from before the check names neither and is taken at its word,
        // and a save the panel already has is answered before the map is looked at.
        private bool IsAnotherMap(string body)
        {
            string? clientId = Field(body, "client_id");
            string? map = Field(body, "map");
            string? workshopId = Field(body, "workshop_id");

            lock (_lock)
            {
                return _checksMaps
                    && !_rows.Any(row => row.ClientId == clientId)
                    && (map != null || workshopId != null)
                    && !string.Equals(map, _map, StringComparison.OrdinalIgnoreCase)
                    && (workshopId == null || workshopId != _workshopId);
            }
        }

        private (int, string)? RefusedSave(string body)
        {
            string? clientId = Field(body, "client_id");
            int? flight = Number(body, "flight_time_ms");

            bool valid =
                Roster.Contains(Field(body, "author_steam_id") ?? "")
                && (clientId == null || Uuid.IsMatch(clientId))
                && (flight == null || (flight >= MinFlightMs && flight <= MaxFlightMs));

            if (!valid)
            {
                return Refused(400);
            }

            return IsAnotherMap(body) ? Refused(409) : null;
        }

        // In the controller's order: the session, then the throw.
        private (int, string)? RefusedResult(string body)
        {
            string? session = Field(body, "session_id");
            string? throwId = Field(body, "throw_id");
            string lineup = Field(body, "utility_lineup_id") ?? "";

            if (session != null && session != SessionId)
            {
                return Refused(409);
            }

            bool valid =
                Roster.Contains(Field(body, "steam_id") ?? "")
                && Uuid.IsMatch(lineup)
                && (throwId == null || Uuid.IsMatch(throwId));

            lock (_lock)
            {
                bool onThisMap = !_lineupMaps.TryGetValue(lineup, out string? map) || map == _map;

                return valid && onThisMap ? null : Refused(400);
            }
        }

        private Step Next(Request request)
        {
            if (_steps.TryDequeue(out Step? step))
            {
                return step;
            }

            bool cameBack = _backFor != null && request.Body.Contains(_backFor);

            return _down && !cameBack ? new Step(Status: 503, Body: "{}") : new Step();
        }

        private (int Status, string Body) Process(Request request)
        {
            lock (_lock)
            {
                if (IsIngest(request))
                {
                    return (201, $"{{\"id\":\"{Saved(request.Body)}\"}}");
                }

                if (IsSession(request))
                {
                    return (200, "{}");
                }

                if (IsResult(request))
                {
                    _counted.Add(Field(request.Body, "throw_id") ?? Guid.NewGuid().ToString());

                    return (201, "{\"attempts\":1}");
                }

                string id = request.Path.Split('/')[2];
                Row? row = _rows.FirstOrDefault(row => row.Id == id);

                if (_refusingDeletes || row == null || row.Author != Query(request, "steam_id"))
                {
                    return (400, "{\"message\":\"refused\"}");
                }

                _rows.Remove(row);

                return (200, "{}");
            }
        }

        private string Saved(string body)
        {
            string? clientId = Field(body, "client_id");
            Row? row = clientId == null ? null : _rows.FirstOrDefault(row => row.ClientId == clientId);

            if (row == null)
            {
                row = new Row(
                    Guid.NewGuid().ToString(),
                    Field(body, "author_steam_id")!,
                    clientId,
                    Field(body, "name") ?? ""
                );

                _rows.Add(row);
            }

            return row.Id;
        }

        private static string? Query(Request request, string name)
        {
            Match value = Regex.Match(request.Query, $"(?:^|&){name}=([^&]*)");

            return value.Success ? Uri.UnescapeDataString(value.Groups[1].Value) : null;
        }

        private static string? Field(string body, string name)
        {
            if (body.Length == 0)
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(body);

            return document.RootElement.TryGetProperty(name, out JsonElement value)
                ? value.GetString()
                : null;
        }

        private static int? Number(string body, string name)
        {
            using JsonDocument document = JsonDocument.Parse(body);

            return document.RootElement.TryGetProperty(name, out JsonElement value)
                ? value.GetInt32()
                : null;
        }

        private static async Task<Request> Read(NetworkStream stream)
        {
            byte[] buffer = new byte[8192];
            List<byte> received = new();
            string head = "";

            while (!head.Contains("\r\n\r\n"))
            {
                int read = await stream.ReadAsync(buffer);
                received.AddRange(buffer.Take(read));
                head = Encoding.ASCII.GetString(received.ToArray());
            }

            int headers = head.IndexOf("\r\n\r\n") + 4;
            Match declared = Regex.Match(head, @"content-length: (\d+)", RegexOptions.IgnoreCase);
            int length = declared.Success ? int.Parse(declared.Groups[1].Value) : 0;

            while (received.Count < headers + length)
            {
                int read = await stream.ReadAsync(buffer);
                received.AddRange(buffer.Take(read));
            }

            string[] line = head.Split("\r\n")[0].Split(' ');
            string[] target = line[1].Split('?', 2);

            return new Request(
                line[0],
                target[0],
                target.Length > 1 ? target[1] : "",
                Regex.Match(head, @"x-server-api-password: ([^\r]*)", RegexOptions.IgnoreCase)
                    .Groups[1]
                    .Value,
                Encoding.UTF8.GetString(received.ToArray(), headers, length)
            );
        }

        private static void Reset(TcpClient connection)
        {
            connection.LingerState = new LingerOption(true, 0);
            connection.Close();
        }

        private static void Write(TcpClient connection, int status, string body)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(
                $"HTTP/1.1 {status} Answer\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}"
            );

            connection.GetStream().Write(bytes);
        }
    }
}
