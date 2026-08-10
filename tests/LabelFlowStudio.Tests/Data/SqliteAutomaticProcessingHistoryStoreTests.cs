using LabelFlowStudio.Core.Statistics;
using LabelFlowStudio.Data.Statistics;
using Microsoft.Data.Sqlite;

namespace LabelFlowStudio.Application.Tests.Data;

public sealed class SqliteAutomaticProcessingHistoryStoreTests
{
    [Fact]
    public async Task History_PersistsAcrossStoreInstances_AndAggregatesUtcRange()
    {
        using var database = new TemporarySqliteDatabase();
        var firstStore = new SqliteAutomaticProcessingHistoryStore(database.DatabasePath);
        var rangeStart = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

        var firstCompletion = rangeStart.AddMinutes(10);
        var secondCompletion = rangeStart.AddMinutes(20);
        var thirdCompletion = rangeStart.AddMinutes(35);

        Assert.True(await firstStore.TryAppendAsync(
            CreateAttempt("4430558", firstCompletion, AutomaticProcessingOutcome.Success),
            CancellationToken.None));
        Assert.True(await firstStore.TryAppendAsync(
            CreateAttempt("4430559", secondCompletion, AutomaticProcessingOutcome.Warning),
            CancellationToken.None));
        Assert.True(await firstStore.TryAppendAsync(
            CreateAttempt("4430560", thirdCompletion, AutomaticProcessingOutcome.Error),
            CancellationToken.None));
        Assert.True(await firstStore.TryAppendAsync(
            CreateAttempt("4430561", rangeStart.AddDays(1), AutomaticProcessingOutcome.Success),
            CancellationToken.None));

        var reopenedStore = new SqliteAutomaticProcessingHistoryStore(database.DatabasePath);
        AutomaticProcessingHistoryAggregate aggregate = await reopenedStore.GetAggregateAsync(
            rangeStart,
            rangeStart.AddDays(1),
            CancellationToken.None);

        Assert.Equal(3, aggregate.CompletedCount);
        Assert.Equal(1, aggregate.SuccessCount);
        Assert.Equal(1, aggregate.ErrorCount);
        Assert.Equal(firstCompletion, aggregate.FirstCompletedAtUtc);
        Assert.Equal(thirdCompletion, aggregate.LastCompletedAtUtc);

        AutomaticProcessingHistoryAggregate nextDayAggregate = await reopenedStore.GetAggregateAsync(
            rangeStart.AddDays(1),
            rangeStart.AddDays(2),
            CancellationToken.None);

        Assert.Equal(1, nextDayAggregate.CompletedCount);
        Assert.Equal(1, nextDayAggregate.SuccessCount);
        Assert.Equal(0, nextDayAggregate.ErrorCount);
    }

    [Fact]
    public async Task TryAppendAsync_IsIdempotentByAttemptId()
    {
        using var database = new TemporarySqliteDatabase();
        var store = new SqliteAutomaticProcessingHistoryStore(database.DatabasePath);
        var completedAt = new DateTimeOffset(2026, 8, 10, 6, 0, 0, TimeSpan.Zero);
        Guid attemptId = Guid.NewGuid();
        var original = CreateAttempt(
            "4430558",
            completedAt,
            AutomaticProcessingOutcome.Success,
            attemptId);
        var duplicate = CreateAttempt(
            "changed",
            completedAt.AddMinutes(1),
            AutomaticProcessingOutcome.Error,
            attemptId);

        Assert.True(await store.TryAppendAsync(original, CancellationToken.None));
        Assert.False(await store.TryAppendAsync(duplicate, CancellationToken.None));

        AutomaticProcessingHistoryAggregate aggregate = await store.GetAggregateAsync(
            completedAt.AddHours(-1),
            completedAt.AddHours(1),
            CancellationToken.None);
        Assert.Equal(1, aggregate.CompletedCount);
        Assert.Equal(1, aggregate.SuccessCount);
        Assert.Equal(0, aggregate.ErrorCount);
        Assert.Equal(completedAt, aggregate.FirstCompletedAtUtc);
        Assert.Equal(completedAt, aggregate.LastCompletedAtUtc);
    }

    [Fact]
    public async Task GetAggregateAsync_EmptyRange_ReturnsZeroCountsAndNoBoundaries()
    {
        using var database = new TemporarySqliteDatabase();
        var store = new SqliteAutomaticProcessingHistoryStore(database.DatabasePath);
        var rangeStart = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

        AutomaticProcessingHistoryAggregate aggregate = await store.GetAggregateAsync(
            rangeStart,
            rangeStart.AddDays(1),
            CancellationToken.None);

        Assert.Equal(0, aggregate.CompletedCount);
        Assert.Equal(0, aggregate.SuccessCount);
        Assert.Equal(0, aggregate.ErrorCount);
        Assert.Null(aggregate.FirstCompletedAtUtc);
        Assert.Null(aggregate.LastCompletedAtUtc);
    }

    private static AutomaticProcessingAttempt CreateAttempt(
        string tenam,
        DateTimeOffset completedAtUtc,
        AutomaticProcessingOutcome outcome,
        Guid? attemptId = null)
    {
        return new AutomaticProcessingAttempt(
            attemptId ?? Guid.NewGuid(),
            tenam,
            completedAtUtc.AddSeconds(-2),
            completedAtUtc,
            outcome);
    }

    private sealed class TemporarySqliteDatabase : IDisposable
    {
        internal TemporarySqliteDatabase()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "LabelFlowStudio.Tests",
                Guid.NewGuid().ToString("N"));
            DatabasePath = Path.Combine(DirectoryPath, "LabelFlowStudio.db");
        }

        private string DirectoryPath { get; }

        internal string DatabasePath { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
