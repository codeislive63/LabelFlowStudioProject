using LabelFlowStudio.Core.Abstractions;
using LabelFlowStudio.Core.Statistics;

namespace LabelFlowStudio.Application.Statistics;

/// <summary>
/// Coordinates automatic-processing history and calculates operator-facing KPIs.
/// </summary>
public sealed class AutomaticProcessingStatisticsService : IAutomaticProcessingStatisticsService
{
    private static readonly TimeSpan MinimumThroughputWindow = TimeSpan.FromSeconds(1);

    private readonly IAutomaticProcessingHistoryStore _historyStore;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _localTimeZone;

    public AutomaticProcessingStatisticsService(
        IAutomaticProcessingHistoryStore historyStore,
        TimeProvider? timeProvider = null,
        TimeZoneInfo? localTimeZone = null)
    {
        _historyStore = historyStore ?? throw new ArgumentNullException(nameof(historyStore));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _localTimeZone = localTimeZone ?? TimeZoneInfo.Local;
    }

    public event EventHandler? StatisticsChanged;

    public DateOnly CurrentLocalDate => GetLocalDate(_timeProvider.GetUtcNow());

    public AutomaticProcessingAttemptContext BeginAttempt(string tenam)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenam);

        return new AutomaticProcessingAttemptContext(
            Guid.NewGuid(),
            tenam.Trim(),
            _timeProvider.GetUtcNow().ToUniversalTime());
    }

    public async Task<bool> CompleteAttemptAsync(
        AutomaticProcessingAttemptContext context,
        AutomaticProcessingOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.AttemptId == Guid.Empty)
        {
            throw new ArgumentException("Attempt ID must not be empty.", nameof(context));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(context.Tenam);

        var attempt = new AutomaticProcessingAttempt(
            context.AttemptId,
            context.Tenam,
            context.StartedAtUtc.ToUniversalTime(),
            _timeProvider.GetUtcNow().ToUniversalTime(),
            outcome);

        var inserted = await _historyStore
            .TryAppendAsync(attempt, cancellationToken)
            .ConfigureAwait(false);

        if (inserted)
        {
            StatisticsChanged?.Invoke(this, EventArgs.Empty);
        }

        return inserted;
    }

    public async Task<AutomaticProcessingKpiSnapshot> GetCurrentDayAsync(
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        var localDate = GetLocalDate(nowUtc);
        var fromInclusiveUtc = ConvertLocalBoundaryToUtc(localDate);
        var nextLocalMidnightUtc = ConvertLocalBoundaryToUtc(localDate.AddDays(1));

        // SQLite stores completion timestamps as Unix milliseconds. Advancing the
        // exclusive bound by one millisecond includes attempts completed in the
        // clock's current millisecond without admitting future rows.
        var throughCurrentMillisecondExclusive = DateTimeOffset
            .FromUnixTimeMilliseconds(nowUtc.ToUnixTimeMilliseconds())
            .AddMilliseconds(1);
        var toExclusiveUtc = throughCurrentMillisecondExclusive < nextLocalMidnightUtc
            ? throughCurrentMillisecondExclusive
            : nextLocalMidnightUtc;

        var aggregate = await _historyStore
            .GetAggregateAsync(fromInclusiveUtc, toExclusiveUtc, cancellationToken)
            .ConfigureAwait(false);

        return new AutomaticProcessingKpiSnapshot(
            localDate,
            aggregate.CompletedCount,
            aggregate.SuccessCount,
            aggregate.ErrorCount,
            CalculateBoxesPerHour(aggregate));
    }

    private DateOnly GetLocalDate(DateTimeOffset instant)
    {
        var localTime = TimeZoneInfo.ConvertTime(instant, _localTimeZone);
        return DateOnly.FromDateTime(localTime.DateTime);
    }

    private DateTimeOffset ConvertLocalBoundaryToUtc(DateOnly localDate)
    {
        var localTime = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        // A few time zones have historically advanced their clocks at midnight.
        // Move an invalid boundary to the first representable local instant.
        while (_localTimeZone.IsInvalidTime(localTime))
        {
            localTime = localTime.AddMinutes(1);
        }

        if (_localTimeZone.IsAmbiguousTime(localTime))
        {
            // The larger UTC offset denotes the first occurrence of an ambiguous wall time.
            var firstOffset = _localTimeZone.GetAmbiguousTimeOffsets(localTime).Max();
            return new DateTimeOffset(localTime, firstOffset).ToUniversalTime();
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(localTime, _localTimeZone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static double? CalculateBoxesPerHour(
        AutomaticProcessingHistoryAggregate aggregate)
    {
        if (aggregate.CompletedCount < 2 ||
            aggregate.FirstCompletedAtUtc is not { } firstCompletedAtUtc ||
            aggregate.LastCompletedAtUtc is not { } lastCompletedAtUtc)
        {
            return null;
        }

        var elapsed = lastCompletedAtUtc - firstCompletedAtUtc;
        if (elapsed < MinimumThroughputWindow)
        {
            return null;
        }

        var boxesPerHour = (aggregate.CompletedCount - 1) / elapsed.TotalHours;
        return double.IsFinite(boxesPerHour) && boxesPerHour >= 0
            ? boxesPerHour
            : null;
    }
}
