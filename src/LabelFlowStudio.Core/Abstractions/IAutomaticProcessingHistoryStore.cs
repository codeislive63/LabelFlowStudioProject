using LabelFlowStudio.Core.Statistics;

namespace LabelFlowStudio.Core.Abstractions;

/// <summary>
/// Хранилище итогов автоматической обработки коробов.
/// </summary>
public interface IAutomaticProcessingHistoryStore
{
    /// <summary>
    /// Добавляет завершённую попытку, если запись с таким идентификатором ещё отсутствует.
    /// </summary>
    Task<bool> TryAppendAsync(
        AutomaticProcessingAttempt attempt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Возвращает агрегат по времени завершения в полуоткрытом UTC-интервале.
    /// </summary>
    Task<AutomaticProcessingHistoryAggregate> GetAggregateAsync(
        DateTimeOffset fromInclusiveUtc,
        DateTimeOffset toExclusiveUtc,
        CancellationToken cancellationToken);
}
