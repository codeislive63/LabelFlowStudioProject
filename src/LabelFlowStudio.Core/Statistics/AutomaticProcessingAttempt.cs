namespace LabelFlowStudio.Core.Statistics;

/// <summary>
/// Завершённая попытка автоматической обработки одного TENAM.
/// </summary>
public sealed record AutomaticProcessingAttempt(
    Guid AttemptId,
    string Tenam,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    AutomaticProcessingOutcome Outcome);
