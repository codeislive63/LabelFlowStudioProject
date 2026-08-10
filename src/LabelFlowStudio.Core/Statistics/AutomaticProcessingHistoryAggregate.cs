namespace LabelFlowStudio.Core.Statistics;

/// <summary>
/// Агрегированная статистика завершённых автоматических обработок за интервал.
/// </summary>
public sealed record AutomaticProcessingHistoryAggregate(
    long CompletedCount,
    long SuccessCount,
    long ErrorCount,
    DateTimeOffset? FirstCompletedAtUtc,
    DateTimeOffset? LastCompletedAtUtc);
