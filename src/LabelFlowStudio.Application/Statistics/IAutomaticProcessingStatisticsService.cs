using LabelFlowStudio.Core.Statistics;

namespace LabelFlowStudio.Application.Statistics;

/// <summary>
/// Records completed automatic attempts and provides current-local-day KPIs.
/// </summary>
public interface IAutomaticProcessingStatisticsService
{
    /// <summary>
    /// Raised after a new attempt has been persisted successfully.
    /// </summary>
    event EventHandler? StatisticsChanged;

    /// <summary>
    /// Gets the calendar date in the workstation's local time zone.
    /// </summary>
    DateOnly CurrentLocalDate { get; }

    /// <summary>
    /// Starts tracking a real automatic-processing attempt.
    /// </summary>
    AutomaticProcessingAttemptContext BeginAttempt(string tenam);

    /// <summary>
    /// Persists the final outcome once. Returns false when the same attempt was already recorded.
    /// </summary>
    Task<bool> CompleteAttemptAsync(
        AutomaticProcessingAttemptContext context,
        AutomaticProcessingOutcome outcome,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets automatic-line KPIs from local midnight through the current moment.
    /// </summary>
    Task<AutomaticProcessingKpiSnapshot> GetCurrentDayAsync(
        CancellationToken cancellationToken = default);
}
