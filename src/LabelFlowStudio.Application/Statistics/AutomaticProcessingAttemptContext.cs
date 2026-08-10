namespace LabelFlowStudio.Application.Statistics;

/// <summary>
/// Identifies one automatic box-processing attempt from its actual start until completion.
/// </summary>
public sealed record AutomaticProcessingAttemptContext(
    Guid AttemptId,
    string Tenam,
    DateTimeOffset StartedAtUtc);
