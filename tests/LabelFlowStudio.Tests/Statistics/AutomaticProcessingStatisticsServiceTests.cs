using LabelFlowStudio.Application;
using LabelFlowStudio.Application.Statistics;
using LabelFlowStudio.Core.Abstractions;
using LabelFlowStudio.Core.Statistics;
using Microsoft.Extensions.DependencyInjection;

namespace LabelFlowStudio.Tests.Statistics;

public sealed class AutomaticProcessingStatisticsServiceTests
{
    [Fact]
    public void BeginAttempt_CreatesUniqueContextWithTrimmedTenamAndUtcTimestamp()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 30, 0, TimeSpan.FromHours(3));
        var clock = new MutableTimeProvider(now);
        var service = CreateService(new RecordingHistoryStore(), clock, TimeZoneInfo.Utc);

        var first = service.BeginAttempt(" 4430558 ");
        var second = service.BeginAttempt("4430558");

        Assert.NotEqual(Guid.Empty, first.AttemptId);
        Assert.NotEqual(first.AttemptId, second.AttemptId);
        Assert.Equal("4430558", first.Tenam);
        Assert.Equal(now.ToUniversalTime(), first.StartedAtUtc);
        Assert.Equal(TimeSpan.Zero, first.StartedAtUtc.Offset);
    }

    [Fact]
    public async Task CompleteAttemptAsync_PersistsTypedFinalOutcomeOnceAndRaisesOneEvent()
    {
        var startedAt = new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddMinutes(2);
        var clock = new MutableTimeProvider(startedAt);
        var store = new RecordingHistoryStore();
        var service = CreateService(store, clock, TimeZoneInfo.Utc);
        var notifications = 0;
        service.StatisticsChanged += (_, _) => notifications++;

        var context = service.BeginAttempt("4430558");
        clock.SetUtcNow(completedAt);

        var inserted = await service.CompleteAttemptAsync(
            context,
            AutomaticProcessingOutcome.Warning,
            CancellationToken.None);
        var duplicateInserted = await service.CompleteAttemptAsync(
            context,
            AutomaticProcessingOutcome.Warning,
            CancellationToken.None);

        Assert.True(inserted);
        Assert.False(duplicateInserted);
        Assert.Equal(1, notifications);

        var persisted = Assert.Single(store.PersistedAttempts);
        Assert.Equal(context.AttemptId, persisted.AttemptId);
        Assert.Equal("4430558", persisted.Tenam);
        Assert.Equal(startedAt, persisted.StartedAtUtc);
        Assert.Equal(completedAt, persisted.CompletedAtUtc);
        Assert.Equal(AutomaticProcessingOutcome.Warning, persisted.Outcome);
    }

    [Fact]
    public async Task GetCurrentDayAsync_UsesUtcBoundariesForLocalDayAcrossOffsetChange()
    {
        var timeZone = CreateDaylightSavingTimeZone();
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 3, 8, 16, 0, 0, TimeSpan.Zero));
        var store = new RecordingHistoryStore
        {
            AggregateOverride = new AutomaticProcessingHistoryAggregate(7, 5, 1, null, null),
        };
        var service = CreateService(store, clock, timeZone);

        var snapshot = await service.GetCurrentDayAsync(CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 3, 8), snapshot.LocalDate);
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero), store.FromInclusiveUtc);
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 16, 0, 0, 1, TimeSpan.Zero), store.ToExclusiveUtc);
        Assert.Equal(7, snapshot.CompletedCount);
        Assert.Equal(5, snapshot.SuccessCount);
        Assert.Equal(1, snapshot.ErrorCount);
    }

    [Fact]
    public async Task GetCurrentDayAsync_ReturnsNoSpeedForNoOrOneCompletion()
    {
        var store = new RecordingHistoryStore();
        var service = CreateService(
            store,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.Zero)),
            TimeZoneInfo.Utc);

        var empty = await service.GetCurrentDayAsync(CancellationToken.None);

        store.AggregateOverride = new AutomaticProcessingHistoryAggregate(
            1,
            1,
            0,
            new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero));
        var single = await service.GetCurrentDayAsync(CancellationToken.None);

        Assert.Null(empty.BoxesPerHour);
        Assert.Null(single.BoxesPerHour);
    }

    [Fact]
    public async Task GetCurrentDayAsync_CalculatesSpeedFromFirstToLastCompletion()
    {
        var store = new RecordingHistoryStore
        {
            AggregateOverride = new AutomaticProcessingHistoryAggregate(
                6,
                4,
                1,
                new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 10, 9, 30, 0, TimeSpan.Zero)),
        };
        var service = CreateService(
            store,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.Zero)),
            TimeZoneInfo.Utc);

        var snapshot = await service.GetCurrentDayAsync(CancellationToken.None);

        Assert.Equal(10d, snapshot.BoxesPerHour);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public async Task GetCurrentDayAsync_ReturnsNoSpeedForTinyElapsedWindow(int elapsedMilliseconds)
    {
        var first = new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);
        var store = new RecordingHistoryStore
        {
            AggregateOverride = new AutomaticProcessingHistoryAggregate(
                2,
                2,
                0,
                first,
                first.AddMilliseconds(elapsedMilliseconds)),
        };
        var service = CreateService(
            store,
            new MutableTimeProvider(first.AddHours(1)),
            TimeZoneInfo.Utc);

        var snapshot = await service.GetCurrentDayAsync(CancellationToken.None);

        Assert.Null(snapshot.BoxesPerHour);
    }

    [Fact]
    public async Task GetCurrentDayAsync_IncludesCurrentMillisecondAndExcludesFutureRows()
    {
        var now = new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.Zero);
        var store = new RecordingHistoryStore();
        store.SeedAttempt(CreateAttempt(now.AddHours(-1), AutomaticProcessingOutcome.Success));
        store.SeedAttempt(CreateAttempt(now, AutomaticProcessingOutcome.Error));
        store.SeedAttempt(CreateAttempt(now.AddHours(1), AutomaticProcessingOutcome.Warning));
        var service = CreateService(store, new MutableTimeProvider(now), TimeZoneInfo.Utc);

        var snapshot = await service.GetCurrentDayAsync(CancellationToken.None);

        Assert.Equal(2, snapshot.CompletedCount);
        Assert.Equal(1, snapshot.SuccessCount);
        Assert.Equal(1, snapshot.ErrorCount);
        Assert.Equal(now.AddMilliseconds(1), store.ToExclusiveUtc);
    }

    [Fact]
    public async Task GetCurrentDayAsync_CapsUpperBoundAtNextLocalMidnight()
    {
        var timeZone = CreateDaylightSavingTimeZone();
        var finalMillisecondOfLocalDay =
            new DateTimeOffset(2026, 3, 9, 3, 59, 59, 999, TimeSpan.Zero);
        var store = new RecordingHistoryStore();
        var service = CreateService(
            store,
            new MutableTimeProvider(finalMillisecondOfLocalDay),
            timeZone);

        await service.GetCurrentDayAsync(CancellationToken.None);

        Assert.Equal(new DateTimeOffset(2026, 3, 9, 4, 0, 0, TimeSpan.Zero), store.ToExclusiveUtc);
        Assert.Equal(TimeSpan.FromHours(23), store.ToExclusiveUtc - store.FromInclusiveUtc);
    }

    [Fact]
    public void CurrentLocalDate_ChangesWhenTheWorkstationCalendarDayChanges()
    {
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 10, 23, 59, 59, TimeSpan.Zero));
        var service = CreateService(new RecordingHistoryStore(), clock, TimeZoneInfo.Utc);

        Assert.Equal(new DateOnly(2026, 8, 10), service.CurrentLocalDate);

        clock.SetUtcNow(new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 8, 11), service.CurrentLocalDate);
    }

    [Fact]
    public void AddLabelFlowApplication_RegistersClockAndStatisticsServiceAsSingletons()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAutomaticProcessingHistoryStore>(new RecordingHistoryStore());

        services.AddLabelFlowApplication();

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IAutomaticProcessingStatisticsService>();
        var second = provider.GetRequiredService<IAutomaticProcessingStatisticsService>();

        Assert.Same(first, second);
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    private static AutomaticProcessingStatisticsService CreateService(
        IAutomaticProcessingHistoryStore store,
        TimeProvider clock,
        TimeZoneInfo timeZone) => new(store, clock, timeZone);

    private static AutomaticProcessingAttempt CreateAttempt(
        DateTimeOffset completedAtUtc,
        AutomaticProcessingOutcome outcome) => new(
            Guid.NewGuid(),
            "4430558",
            completedAtUtc.AddSeconds(-1),
            completedAtUtc,
            outcome);

    private static TimeZoneInfo CreateDaylightSavingTimeZone()
    {
        var daylightStart = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0),
            3,
            2,
            DayOfWeek.Sunday);
        var daylightEnd = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
            new DateTime(1, 1, 1, 2, 0, 0),
            11,
            1,
            DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2020, 1, 1),
            new DateTime(2030, 12, 31),
            TimeSpan.FromHours(1),
            daylightStart,
            daylightEnd);

        return TimeZoneInfo.CreateCustomTimeZone(
            "LabelFlowStudio.Tests.Local",
            TimeSpan.FromHours(-5),
            "Test local time",
            "Test standard time",
            "Test daylight time",
            [rule]);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow.ToUniversalTime();
    }

    private sealed class RecordingHistoryStore : IAutomaticProcessingHistoryStore
    {
        private readonly HashSet<Guid> _attemptIds = [];

        public AutomaticProcessingHistoryAggregate? AggregateOverride { get; set; }

        public List<AutomaticProcessingAttempt> PersistedAttempts { get; } = [];

        public DateTimeOffset FromInclusiveUtc { get; private set; }

        public DateTimeOffset ToExclusiveUtc { get; private set; }

        public void SeedAttempt(AutomaticProcessingAttempt attempt)
        {
            _attemptIds.Add(attempt.AttemptId);
            PersistedAttempts.Add(attempt);
        }

        public Task<bool> TryAppendAsync(
            AutomaticProcessingAttempt attempt,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_attemptIds.Add(attempt.AttemptId))
            {
                return Task.FromResult(false);
            }

            PersistedAttempts.Add(attempt);
            return Task.FromResult(true);
        }

        public Task<AutomaticProcessingHistoryAggregate> GetAggregateAsync(
            DateTimeOffset fromInclusiveUtc,
            DateTimeOffset toExclusiveUtc,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FromInclusiveUtc = fromInclusiveUtc;
            ToExclusiveUtc = toExclusiveUtc;

            if (AggregateOverride is { } aggregate)
            {
                return Task.FromResult(aggregate);
            }

            var attempts = PersistedAttempts
                .Where(attempt =>
                    attempt.CompletedAtUtc >= fromInclusiveUtc &&
                    attempt.CompletedAtUtc < toExclusiveUtc)
                .OrderBy(attempt => attempt.CompletedAtUtc)
                .ToList();

            return Task.FromResult(new AutomaticProcessingHistoryAggregate(
                attempts.Count,
                attempts.LongCount(attempt => attempt.Outcome == AutomaticProcessingOutcome.Success),
                attempts.LongCount(attempt => attempt.Outcome == AutomaticProcessingOutcome.Error),
                attempts.FirstOrDefault()?.CompletedAtUtc,
                attempts.LastOrDefault()?.CompletedAtUtc));
        }
    }
}
