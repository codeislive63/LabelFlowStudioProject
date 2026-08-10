namespace LabelFlowStudio.Application.Statistics;

/// <summary>
/// Aggregated automatic-line statistics for one local calendar day.
/// </summary>
public sealed record AutomaticProcessingKpiSnapshot(
    DateOnly LocalDate,
    long CompletedCount,
    long SuccessCount,
    long ErrorCount,
    double? BoxesPerHour);
